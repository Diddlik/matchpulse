using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace MatchPulse.Tests;

// The OBS plugin against a fake OBS WebSocket v5 server on localhost.
public sealed class ObsSinkTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 23, 20, 0, 0, TimeSpan.FromHours(1));
    readonly FakeObs _obs = new("secret");

    public void Dispose() => _obs.Dispose();

    ObsSettings Settings(string password = "secret", params ObsRule[] rules) => new(true, "localhost", _obs.Port, password, rules);

    static GameEvent Event(string type, int sequence = 1) =>
        new(type, "m1", sequence, Start, type == "kill" ? new Dictionary<string, string> { ["kills"] = (sequence - 1).ToString() } : new Dictionary<string, string>());

    static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }

    [Fact]
    public void Placeholders_are_filled_from_the_Game_Event()
    {
        var kill = new GameEvent("kill", "91fdb6ce4f2a", 4, Start, new Dictionary<string, string> { ["kills"] = "3" });
        var mpKill = new GameEvent("kill", null, 2, Start, new Dictionary<string, string> { ["kills"] = "2" });
        var ended = new GameEvent("match_ended", "91fdb6ce4f2a", 5, Start, new Dictionary<string, string> { ["result"] = "victory", ["kills"] = "3" });

        Assert.Equal("2026-11-23_91fdb6ce_Kill-3_20-00-00", ObsTemplate.Fill("{date}_{match}_Kill-{kills}_{time}", kill));
        Assert.Equal("mp 2 kills", ObsTemplate.Fill("{match} {kills} kills", mpKill));
        Assert.Equal("victory with 3 kills (#5)", ObsTemplate.Fill("{result} with {kills} kills (#{n})", ended));
        var started = new GameEvent("match_started", "91fdb6ce4f2a", 1, Start, new Dictionary<string, string> { ["mode"] = "RAPID ROYALE – TRIOS", ["map"] = "MIRAK-TAL: FLUSSUFER" });
        Assert.Equal("RAPID ROYALE – TRIOS / MIRAK-TAL: FLUSSUFER", ObsTemplate.Fill("{mode} / {map}", started));
        Assert.Equal(" / ", ObsTemplate.Fill("{mode} / {map}", kill));
    }

    [Fact]
    public async Task Chapter_marker_and_text_take_the_kill_count()
    {
        var sink = new ObsSink(Settings("secret",
            new ObsRule("r1", "kill", "record_chapter"),
            new ObsRule("r2", "kill", "set_text", "Kills", "Kills: {kills}")));

        await sink.SendAsync(Event("kill", sequence: 3), default);

        Assert.Equal(["CreateRecordChapter", "SetInputSettings"], _obs.Requests.Select(r => r.Type));
        Assert.Equal("Kill 2", (string?)_obs.Requests[0].Data!["chapterName"]);
        Assert.Equal(("Kills", "Kills: 2"), ((string?)_obs.Requests[1].Data!["inputName"], (string?)_obs.Requests[1].Data!["inputSettings"]!["text"]));
    }

    [Fact]
    public async Task Source_is_shown_in_the_current_scene_and_hidden_after_its_duration()
    {
        var later = new List<Exception?>();
        var sink = new ObsSink(Settings("secret", new ObsRule("r1", "kill", "show_source", "Kill overlay", DurationSeconds: 0.3)), (_, e) => later.Add(e));

        await sink.SendAsync(Event("kill"), default);
        var shown = _obs.Requests.Last();
        await Until(() => later.Count == 1);

        Assert.Equal(("Game", 7, true), ((string?)shown.Data!["sceneName"], (int)shown.Data["sceneItemId"]!, (bool)shown.Data["sceneItemEnabled"]!));
        Assert.Null(later[0]);
        Assert.False((bool)_obs.Requests.Last().Data!["sceneItemEnabled"]!);
    }

    [Fact]
    public async Task Delayed_rule_runs_later_without_holding_up_the_Game_Event()
    {
        var later = new List<Exception?>();
        var sink = new ObsSink(Settings("secret", new ObsRule("r1", "kill", "save_replay", DelaySeconds: 0.3)), (_, e) => later.Add(e));

        await sink.SendAsync(Event("kill"), default);
        Assert.Empty(_obs.Requests);
        await Until(() => later.Count == 1);

        Assert.Equal("SaveReplayBuffer", Assert.Single(_obs.Requests).Type);
        Assert.Null(later[0]);
    }

    [Fact]
    public async Task Saved_replay_is_renamed_from_its_template()
    {
        var folder = Directory.CreateTempSubdirectory("mp-replays-").FullName;
        try
        {
            _obs.ReplayPath = Path.Combine(folder, "Replay 2026-11-23 20-00-01.mkv");
            File.WriteAllText(_obs.ReplayPath, "");

            await new ObsSink(Settings("secret", new ObsRule("r1", "kill", "save_replay", "{date}_{match}_Kill-{kills}"))).SendAsync(Event("kill", sequence: 2), default);

            Assert.Equal(["2026-11-23_m1_Kill-1.mkv"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Rules_for_the_Game_Event_run_as_OBS_requests_after_authenticating()
    {
        var sink = new ObsSink(Settings("secret",
            new("r1", "match_started", "start_recording"),
            new("r2", "kill", "save_replay"),
            new("r3", "kill", "switch_scene", "Highlight")));

        await sink.SendAsync(Event("kill"), default);

        Assert.True(_obs.Authenticated);
        Assert.Equal(["SaveReplayBuffer", "SetCurrentProgramScene"], _obs.Requests.Select(r => r.Type));
        Assert.Equal("Highlight", (string?)_obs.Requests[1].Data!["sceneName"]);
    }

    [Fact]
    public async Task Game_Event_without_a_rule_does_not_connect()
    {
        await new ObsSink(Settings("secret", new ObsRule("r1", "match_started", "start_recording"))).SendAsync(Event("kill"), default);

        Assert.Empty(_obs.Requests);
        Assert.False(_obs.Authenticated);
    }

    [Fact]
    public async Task Wrong_password_is_reported()
    {
        var error = await Assert.ThrowsAsync<ObsException>(() => ObsSink.TestAsync(Settings("wrong"), default));

        Assert.Contains("password", error.Message);
    }

    [Fact]
    public async Task Starting_a_running_recording_is_fine_but_saving_a_stopped_replay_buffer_fails()
    {
        _obs.Fail["StartRecord"] = 500;
        _obs.Fail["SaveReplayBuffer"] = 501;

        await new ObsSink(Settings("secret", new ObsRule("r1", "match_started", "start_recording"))).SendAsync(Event("match_started"), default);
        var error = await Assert.ThrowsAsync<ObsException>(() => new ObsSink(Settings("secret", new ObsRule("r2", "kill", "save_replay"))).SendAsync(Event("kill"), default));

        // The replay buffer was off: MatchPulse starts it so the next kills are saved, and says so.
        Assert.Equal(501, error.Code);
        Assert.Contains("replay buffer was off", error.Message);
        Assert.Equal(["StartRecord", "SaveReplayBuffer", "StartReplayBuffer"], _obs.Requests.Select(r => r.Type));
    }

    [Fact]
    public async Task Test_returns_the_OBS_version()
    {
        Assert.Equal("31.0.0", await ObsSink.TestAsync(Settings(), default));
    }

    [Fact]
    public void Plugin_from_the_window_maps_to_the_OBS_settings()
    {
        var plugin = JsonNode.Parse("""{"id":"obs","enabled":true,"status":"connected","config":{"host":" 192.168.1.5 ","port":4456,"password":"pw"},"rules":[{"id":"r1","event":"kill","action":"save_replay","param":""}]}""")!.AsObject();

        var obs = UiHub.ObsFrom(plugin);

        Assert.Equal((true, "192.168.1.5", 4456, "pw"), (obs.Enabled, obs.Host, obs.Port, obs.Password));
        Assert.Equal(new ObsRule("r1", "kill", "save_replay"), Assert.Single(obs.Rules));
    }

    /// <summary>Speaks just enough OBS WebSocket v5: Hello with an auth challenge, Identify, requests.</summary>
    sealed class FakeObs : IDisposable
    {
        const string Salt = "c2FsdA==", Challenge = "Y2hhbGxlbmdl";
        readonly HttpListener _listener = new();
        readonly string _password;

        public FakeObs(string password)
        {
            _password = password;
            Port = FreePort();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int Port { get; }
        public bool Authenticated { get; private set; }
        public List<(string Type, JsonNode? Data)> Requests { get; } = [];
        public Dictionary<string, int> Fail { get; } = [];
        public string? ReplayPath { get; set; }

        async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); } catch (Exception) { return; }
                _ = Task.Run(() => ServeAsync(context));
            }
        }

        async Task ServeAsync(HttpListenerContext context)
        {
            {
                var ws = (await context.AcceptWebSocketAsync("obswebsocket.json")).WebSocket;
                await SendAsync(ws, new JsonObject { ["op"] = 0, ["d"] = new JsonObject { ["rpcVersion"] = 1, ["authentication"] = new JsonObject { ["salt"] = Salt, ["challenge"] = Challenge } } });
                var identify = await ReceiveAsync(ws);
                if ((string?)identify!["d"]!["authentication"] != ObsSession.Authentication(_password, Salt, Challenge))
                {
                    await ws.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed.", default);
                    return;
                }
                Authenticated = true;
                await SendAsync(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });
                while (await ReceiveAsync(ws) is { } request)
                {
                    var type = (string)request["d"]!["requestType"]!;
                    lock (Requests) Requests.Add((type, request["d"]!["requestData"]?.DeepClone()));
                    var code = Fail.GetValueOrDefault(type, 100);
                    await SendAsync(ws, new JsonObject
                    {
                        ["op"] = 7,
                        ["d"] = new JsonObject
                        {
                            ["requestType"] = type, ["requestId"] = (string?)request["d"]!["requestId"],
                            ["requestStatus"] = new JsonObject { ["result"] = code == 100, ["code"] = code },
                            ["responseData"] = type switch
                            {
                                "GetVersion" => new JsonObject { ["obsVersion"] = "31.0.0" },
                                "GetCurrentProgramScene" => new JsonObject { ["currentProgramSceneName"] = "Game" },
                                "GetSceneItemId" => new JsonObject { ["sceneItemId"] = 7 },
                                _ => null,
                            },
                        },
                    });
                    if (type == "SaveReplayBuffer" && ReplayPath is { } path)
                        await SendAsync(ws, new JsonObject { ["op"] = 5, ["d"] = new JsonObject { ["eventType"] = "ReplayBufferSaved", ["eventData"] = new JsonObject { ["savedReplayPath"] = path } } });
                }
            }
        }

        static Task SendAsync(WebSocket ws, JsonNode message) =>
            ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, default);

        static async Task<JsonNode?> ReceiveAsync(WebSocket ws)
        {
            var buffer = new byte[65536];
            var result = await ws.ReceiveAsync(buffer, default);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default);
                return null;
            }
            return JsonNode.Parse(buffer.AsSpan(0, result.Count));
        }

        static int FreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        public void Dispose() => _listener.Close();
    }
}
