using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace MatchPulse.Tests;

// The MQTT plugin against an in-process MQTT broker.
public sealed class MqttSinkTests : IAsyncLifetime
{
    static readonly DateTimeOffset Start = new(2026, 11, 23, 20, 0, 0, TimeSpan.FromHours(1));
    readonly int _port = FreePort();
    readonly List<(string Topic, string Payload, bool Retain)> _published = [];
    MqttServer _broker = null!;

    public async Task InitializeAsync()
    {
        var factory = new MqttServerFactory();
        _broker = factory.CreateMqttServer(factory.CreateServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(_port).Build());
        _broker.ValidatingConnectionAsync += e =>
        {
            if (e.UserName is not (null or "" or "player") || e.UserName == "player" && e.Password != "secret")
                e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        _broker.InterceptingPublishAsync += e =>
        {
            lock (_published) _published.Add((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString(), e.ApplicationMessage.Retain));
            return Task.CompletedTask;
        };
        await _broker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _broker.StopAsync();
        _broker.Dispose();
    }

    MqttSettings Settings(string user = "", string password = "") => new(true, "127.0.0.1", _port, false, user, password, "bf6/matchpulse", 1);

    async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            lock (_published) if (condition()) return;
            await Task.Delay(20);
        }
        lock (_published) Assert.True(condition());
    }

    [Fact]
    public async Task Each_Game_Event_is_published_as_JSON_under_its_type()
    {
        await using var sink = new MqttSink(Settings());
        var kill = new GameEvent("kill", "m1", 2, Start, new Dictionary<string, string> { ["kills"] = "1" });

        await sink.SendAsync(kill, default);
        await sink.SendAsync(new GameEvent("death", "m1", 3, Start, new Dictionary<string, string>()), default);

        await Until(() => _published.Count(p => p.Topic.StartsWith("bf6/matchpulse/events/")) == 2);
        var published = _published.First(p => p.Topic == "bf6/matchpulse/events/kill");
        Assert.Equal(kill.ToJson(), published.Payload);
        Assert.False(published.Retain);
        Assert.Contains(_published, p => p.Topic == "bf6/matchpulse/events/death");
    }

    [Fact]
    public async Task Status_is_online_while_connected_and_offline_after_closing()
    {
        var sink = new MqttSink(Settings());
        await sink.SendAsync(new GameEvent("match_started", "m1", 1, Start, new Dictionary<string, string>()), default);
        await Until(() => _published.Any(p => p.Topic == "bf6/matchpulse/status"));
        Assert.Equal(("online", true), _published.Where(p => p.Topic == "bf6/matchpulse/status").Select(p => (p.Payload, p.Retain)).Last());

        await sink.DisposeAsync();

        await Until(() => _published.Last(p => p.Topic == "bf6/matchpulse/status").Payload == "offline");
        Assert.True(_published.Last(p => p.Topic == "bf6/matchpulse/status").Retain);
    }

    [Fact]
    public async Task Login_with_user_and_password_works_and_a_wrong_password_is_refused()
    {
        await MqttSink.TestAsync(Settings("player", "secret"), default);

        await Assert.ThrowsAnyAsync<Exception>(() => MqttSink.TestAsync(Settings("player", "wrong"), default));
    }

    [Fact]
    public async Task Unreachable_broker_fails_the_delivery()
    {
        await using var sink = new MqttSink(Settings() with { Port = FreePort() });

        await Assert.ThrowsAnyAsync<Exception>(() => sink.SendAsync(new GameEvent("kill", null, 1, Start, new Dictionary<string, string>()), default));
    }

    [Fact]
    public void Plugin_from_the_window_maps_to_the_MQTT_settings()
    {
        var plugin = JsonNode.Parse("""{"id":"mqtt","enabled":true,"config":{"host":" broker.lan ","port":8883,"useTls":true,"username":"u","password":"p","topicPrefix":"home/bf6","qos":2},"rules":[]}""")!.AsObject();

        Assert.Equal(new MqttSettings(true, "broker.lan", 8883, true, "u", "p", "home/bf6", 2), UiHub.MqttFrom(plugin));
    }

    [Fact]
    public async Task State_topics_follow_the_Match_and_are_retained()
    {
        await using var sink = new MqttSink(Settings());
        var empty = new Dictionary<string, string>();

        await sink.SendAsync(new GameEvent("match_started", "m1", 1, Start, empty), default);
        await sink.SendAsync(new GameEvent("kill", "m1", 2, Start, new Dictionary<string, string> { ["kills"] = "1" }), default);
        await sink.SendAsync(new GameEvent("knockdown", "m1", 3, Start, empty), default);
        await sink.SendAsync(new GameEvent("death", "m1", 4, Start, empty), default);

        await Until(() => _published.Count(p => p.Topic == "bf6/matchpulse/state/last_event") == 4);
        string Last(string state) { lock (_published) return _published.Last(p => p.Topic == $"bf6/matchpulse/state/{state}").Payload; }
        Assert.Equal(("ON", "1", "1", "1", "death"), (Last("match"), Last("kills"), Last("knockdowns"), Last("deaths"), Last("last_event")));
        Assert.All(_published.Where(p => p.Topic.Contains("/state/")), p => Assert.True(p.Retain));
    }

    [Fact]
    public async Task Home_Assistant_discovery_announces_the_device_at_start_without_waiting_for_an_event()
    {
        var results = new List<Exception?>();
        await using var sink = new MqttSink(Settings(), new HomeAssistantSettings(true, "ha"), (_, e) => results.Add(e));

        await Until(() => results.Count == 1);
        Assert.Null(results[0]);
        (string Topic, string Payload, bool Retain)[] configs;
        lock (_published) configs = [.. _published.Where(p => p.Topic.StartsWith("ha/"))];

        Assert.Equal(10, configs.Length); // 1 binary sensor, 4 sensors, 5 device triggers
        Assert.All(configs, c => Assert.True(c.Retain));
        var match = JsonNode.Parse(configs.Single(c => c.Topic == "ha/binary_sensor/bf6_matchpulse/match/config").Payload)!;
        Assert.Equal(("bf6/matchpulse/state/match", "bf6/matchpulse/status", "MatchPulse"),
            ((string?)match["state_topic"], (string?)match["availability_topic"], (string?)match["device"]!["name"]));
        var kill = JsonNode.Parse(configs.Single(c => c.Topic == "ha/device_automation/bf6_matchpulse/kill/config").Payload)!;
        Assert.Equal(("trigger", "bf6/matchpulse/events/kill"), ((string?)kill["automation_type"], (string?)kill["topic"]));
    }

    [Fact]
    public async Task Home_Assistant_turned_off_removes_the_device()
    {
        await using var sink = new MqttSink(Settings(), new HomeAssistantSettings(false, "ha"));

        await sink.SendAsync(new GameEvent("kill", null, 1, Start, new Dictionary<string, string> { ["kills"] = "1" }), default);

        lock (_published)
        {
            var configs = _published.Where(p => p.Topic.StartsWith("ha/")).ToList();
            Assert.Equal(10, configs.Count);
            Assert.All(configs, c => Assert.Equal(("", true), (c.Payload ?? "", c.Retain))); // an empty retained message deletes the config
        }
    }

    [Fact]
    public void Home_Assistant_plugin_from_the_window_maps_to_its_settings()
    {
        var plugin = JsonNode.Parse("""{"id":"homeassistant","enabled":true,"config":{"discoveryPrefix":" homeassistant ","topicPrefix":"matchpulse","mqttEnabled":true},"rules":[]}""")!.AsObject();

        Assert.Equal(new HomeAssistantSettings(true, "homeassistant"), UiHub.HomeAssistantFrom(plugin));
        Assert.Equal("bf6_matchpulse", HomeAssistant.NodeId("BF6/MatchPulse"));
    }

    static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
