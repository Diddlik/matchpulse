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

    static GameEvent Event(string type) => new(type, "m1", 1, Start, new Dictionary<string, string>());

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

        Assert.Contains("SaveReplayBuffer", error.Message);
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

        async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); } catch (Exception) { return; }
                var ws = (await context.AcceptWebSocketAsync("obswebsocket.json")).WebSocket;
                await SendAsync(ws, new JsonObject { ["op"] = 0, ["d"] = new JsonObject { ["rpcVersion"] = 1, ["authentication"] = new JsonObject { ["salt"] = Salt, ["challenge"] = Challenge } } });
                var identify = await ReceiveAsync(ws);
                if ((string?)identify!["d"]!["authentication"] != ObsClient.Authentication(_password, Salt, Challenge))
                {
                    await ws.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed.", default);
                    continue;
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
                            ["responseData"] = type == "GetVersion" ? new JsonObject { ["obsVersion"] = "31.0.0" } : null,
                        },
                    });
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
