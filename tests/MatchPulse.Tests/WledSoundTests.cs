using System.Net;
using System.Text.Json.Nodes;

namespace MatchPulse.Tests;

// WLED against an in-memory controller, and the sound plugin with a recording player.
public sealed class WledSoundTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 23, 20, 0, 0, TimeSpan.FromHours(1));
    static GameEvent Event(string type) => new(type, "m1", 2, Start, new Dictionary<string, string>());

    static WledDevice Wled(params WledRule[] rules) => new("d1", "Desk", "192.168.1.50", rules);

    [Fact]
    public async Task Flash_shows_the_colour_and_restores_the_lights_afterwards()
    {
        var controller = new Controller();
        var later = new List<Exception?>();
        var sink = new WledSink(new HttpClient(controller), Wled(new WledRule("r1", "kill", "flash", "#00FF00", "strobe", 0.2)), (_, e) => later.Add(e));

        await sink.SendAsync(Event("kill"), default);

        var flash = controller.Posted.Single();
        Assert.Equal(("[0,255,0]", 23, 255), (flash["seg"]![0]!["col"]![0]!.ToJsonString(), (int)flash["seg"]![0]!["fx"]!, (int)flash["bri"]!));
        for (var i = 0; i < 100 && later.Count == 0; i++) await Task.Delay(20);
        Assert.Null(Assert.Single(later));
        var restore = controller.Posted.Last();
        Assert.Equal((true, 80, "[255,160,40]", 9), ((bool)restore["on"]!, (int)restore["bri"]!, restore["seg"]![0]!["col"]![0]!.ToJsonString(), (int)restore["seg"]![0]!["fx"]!));
    }

    [Fact]
    public async Task Back_to_back_flashes_restore_the_lights_from_before_the_first()
    {
        var controller = new Controller();
        var later = new List<Exception?>();
        var sink = new WledSink(new HttpClient(controller), Wled(new WledRule("r1", "kill", "flash", DurationSeconds: 0.3)), (_, e) => later.Add(e));

        await sink.SendAsync(Event("kill"), default);
        await sink.SendAsync(Event("kill"), default);
        for (var i = 0; i < 100 && later.Count == 0; i++) await Task.Delay(20);
        await Task.Delay(100);

        Assert.Equal(1, controller.StateReads);
        Assert.Single(later);
        Assert.Equal(80, (int)controller.Posted.Last()["bri"]!);
    }

    [Fact]
    public async Task Preset_and_off_and_events_without_a_rule()
    {
        var controller = new Controller();
        var sink = new WledSink(new HttpClient(controller), Wled(new WledRule("r1", "match_ended", "preset", Preset: 3), new WledRule("r2", "death", "off")));

        await sink.SendAsync(Event("kill"), default);
        await sink.SendAsync(Event("match_ended"), default);
        await sink.SendAsync(Event("death"), default);

        Assert.Equal(["""{"on":true,"ps":3}""", """{"on":false}"""], controller.Posted.Select(p => p.ToJsonString()));
    }

    [Fact]
    public async Task Address_with_scheme_and_slash_is_accepted()
    {
        var controller = new Controller();
        var sink = new WledSink(new HttpClient(controller), new WledDevice("d1", "Desk", " http://192.168.1.50/ ", [new WledRule("r1", "kill", "on")]));

        await sink.SendAsync(Event("kill"), default);

        Assert.Single(controller.Posted);
    }

    [Fact]
    public async Task Unreachable_controller_fails_the_delivery()
    {
        var sink = new WledSink(new HttpClient(new Controller { Down = true }), Wled(new WledRule("r1", "kill", "on")));

        await Assert.ThrowsAsync<HttpRequestException>(() => sink.SendAsync(Event("kill"), default));
    }

    [Fact]
    public async Task Sound_rules_play_their_sound_at_their_volume()
    {
        var played = new List<(string Sound, float Volume)>();
        var sink = new SoundSink(new SoundSettings(true, [new("s1", "kill", "ding", 70), new("s2", "kill", @"C:\Sounds\kill.wav", 100), new("s3", "death", "low", 50)]),
            (sound, volume) => played.Add((sound, volume)));

        await sink.SendAsync(Event("kill"), default);

        Assert.Equal([("ding", 0.7f), (@"C:\Sounds\kill.wav", 1f)], played);
    }

    [Fact]
    public void Built_in_sounds_are_rendered_as_audible_PCM()
    {
        foreach (var name in SoundSettings.BuiltIn)
        {
            var pcm = Tones.Render(name);
            Assert.True(pcm.Length > Tones.Format.AverageBytesPerSecond / 10, name);
            Assert.Contains(pcm, b => b != 0);
        }
    }

    [Fact]
    public void Plugins_from_the_window_map_to_their_settings()
    {
        var wled = JsonNode.Parse("""{"id":"wled","enabled":true,"config":{},"devices":[{"id":"d1","name":" Desk ","host":" 192.168.1.50 ","rules":[{"id":"r1","event":"kill","action":"flash","color":"#ff0000","effect":"blink","durationSeconds":1.5,"preset":1}]}],"rules":[]}""")!.AsObject();
        var sound = JsonNode.Parse("""{"id":"sound","enabled":true,"config":{},"rules":[{"id":"s1","event":"death","sound":" \"C:\\Sounds\\x.mp3\" ","volume":40}]}""")!.AsObject();

        var device = Assert.Single(UiHub.WledFrom(wled).Devices);
        Assert.Equal(("d1", "Desk", "192.168.1.50"), (device.Id, device.Name, device.Host));
        Assert.Equal(new WledRule("r1", "kill", "flash", "#FF0000", "blink", 1.5, 1), Assert.Single(device.Rules));
        Assert.Equal(new SoundRule("s1", "death", @"C:\Sounds\x.mp3", 40), Assert.Single(UiHub.SoundFrom(sound).Rules));
    }

    [Fact]
    public void Old_config_with_one_controller_loads_as_the_first_device_and_saves_as_devices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"matchpulse-wled-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"webhooks":[],"wled":{"enabled":true,"host":"192.168.1.50","rules":[{"id":"r1","event":"kill","action":"on"}]}}""");
            var device = Assert.Single(Config.Load(path).Wled.Devices);
            Assert.Equal(("192.168.1.50", "r1"), (device.Host, Assert.Single(device.Rules).Id));

            Config.Load(path).Save(path);
            var wled = JsonNode.Parse(File.ReadAllText(path))!["wled"]!.AsObject();
            Assert.False(wled.ContainsKey("host") || wled.ContainsKey("rules"));
            Assert.Equal("192.168.1.50", Assert.Single(Config.Load(path).Wled.Devices).Host);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A WLED controller: GET /json/state returns a warm white state, POSTs are recorded.</summary>
    sealed class Controller : HttpMessageHandler
    {
        public bool Down { get; init; }
        public List<JsonNode> Posted { get; } = [];
        public int StateReads { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down) throw new HttpRequestException("No route to host.");
            Assert.StartsWith("http://192.168.1.50/json", request.RequestUri!.ToString());
            if (request.Method == HttpMethod.Get)
            {
                StateReads++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"on":true,"bri":80,"ps":-1,"seg":[{"id":0,"col":[[255,160,40],[0,0,0],[0,0,0]],"fx":9,"sx":128,"pal":0}]}"""),
                };
            }
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            if (request.Content.Headers.ContentLength is null) // like WLED on an ESP8266: no chunked bodies
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            lock (Posted) Posted.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"success":true}""") };
        }
    }
}
