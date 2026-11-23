using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MatchPulse;

/// <summary>
/// OBS plugin as an Event Sink: for each Game Event, runs the matching rules as OBS WebSocket (v5) requests.
/// It connects per Game Event (events are seconds apart), so there is no connection to keep alive or to lose.
/// Starting what already runs, or stopping what does not, is not an error.
/// </summary>
public sealed class ObsSink(ObsSettings settings) : IEventSink
{
    public ObsSettings Settings => settings;

    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        var requests = settings.Rules.Where(r => r.Event == gameEvent.Type).Select(Request).ToList();
        if (requests.Count > 0) await new ObsClient(settings).SendAsync(requests, cancellationToken);
    }

    /// <summary>Connects and asks for the OBS version; throws when OBS cannot be reached or rejects the password.</summary>
    public static async Task<string> TestAsync(ObsSettings settings, CancellationToken cancellationToken)
    {
        var responses = await new ObsClient(settings).SendAsync([new ObsRequest("GetVersion", null)], cancellationToken);
        return (string?)responses[0]?["obsVersion"] ?? "?";
    }

    static ObsRequest Request(ObsRule rule) => rule.Action switch
    {
        "start_recording" => new("StartRecord", null, AlreadyRunning: true),
        "stop_recording" => new("StopRecord", null, AlreadyRunning: true),
        "start_replay_buffer" => new("StartReplayBuffer", null, AlreadyRunning: true),
        "stop_replay_buffer" => new("StopReplayBuffer", null, AlreadyRunning: true),
        "save_replay" => new("SaveReplayBuffer", null),
        "switch_scene" => new("SetCurrentProgramScene", new JsonObject { ["sceneName"] = rule.Param }),
        _ => throw new InvalidDataException($"Unknown OBS action {rule.Action}."),
    };

    public override string ToString() => $"OBS {settings.Host}:{settings.Port}";
}

/// <param name="AlreadyRunning">OBS answering "output already running / not running" counts as done.</param>
public sealed record ObsRequest(string Type, JsonObject? Data, bool AlreadyRunning = false);

public sealed class ObsException(string message) : Exception(message);

/// <summary>A minimal OBS WebSocket v5 client: Hello, Identify (with the password if OBS asks), then requests in order.</summary>
public sealed class ObsClient(ObsSettings settings)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    const int OutputRunning = 500, OutputNotRunning = 501;

    /// <summary>Sends the requests in order and returns each response's data. Throws <see cref="ObsException"/> on a failed request.</summary>
    public async Task<IReadOnlyList<JsonNode?>> SendAsync(IReadOnlyList<ObsRequest> requests, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var ct = timeout.Token;
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("obswebsocket.json");
        await socket.ConnectAsync(new Uri($"ws://{settings.Host}:{settings.Port}"), ct);

        var hello = await ReceiveAsync(socket, ct);
        var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
        if (hello["d"]?["authentication"] is { } auth)
            identify["authentication"] = Authentication(settings.Password, (string)auth["salt"]!, (string)auth["challenge"]!);
        await SendAsync(socket, 1, identify, ct);
        if ((int?)(await ReceiveAsync(socket, ct))["op"] != 2) throw new ObsException("OBS did not accept the connection.");

        var results = new List<JsonNode?>();
        foreach (var request in requests)
        {
            var id = Guid.NewGuid().ToString("N");
            await SendAsync(socket, 6, new JsonObject { ["requestType"] = request.Type, ["requestId"] = id, ["requestData"] = request.Data?.DeepClone() }, ct);
            JsonNode response;
            do response = await ReceiveAsync(socket, ct);
            while ((int?)response["op"] != 7 || (string?)response["d"]?["requestId"] != id);
            var status = response["d"]!["requestStatus"]!;
            var code = (int?)status["code"] ?? 0;
            if ((bool?)status["result"] != true && !(request.AlreadyRunning && code is OutputRunning or OutputNotRunning))
                throw new ObsException($"OBS {request.Type} failed: {(string?)status["comment"] ?? "code " + code}");
            results.Add(response["d"]!["responseData"]?.DeepClone());
        }
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct);
        return results;
    }

    /// <summary>OBS WebSocket v5: base64(sha256(base64(sha256(password + salt)) + challenge)).</summary>
    public static string Authentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    static Task SendAsync(ClientWebSocket socket, int op, JsonObject data, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(new JsonObject { ["op"] = op, ["d"] = data }.ToJsonString()), WebSocketMessageType.Text, true, ct);

    static async Task<JsonNode> ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var message = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new ObsException(socket.CloseStatus == (WebSocketCloseStatus)4009 ? "OBS rejected the password." : $"OBS closed the connection: {socket.CloseStatusDescription}");
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return JsonNode.Parse(message.ToArray())!;
        }
    }
}
