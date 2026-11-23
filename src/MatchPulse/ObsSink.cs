using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MatchPulse;

/// <summary>
/// OBS plugin as an Event Sink: for each Game Event, runs the matching rules over OBS WebSocket (v5).
/// It connects per Game Event (events are seconds apart), so there is no connection to keep alive or to lose.
/// Rules without a delay run before <see cref="SendAsync"/> returns; delayed rules, and hiding a source again, run
/// later on their own connection and report their result to <paramref name="later"/>.
/// Starting what already runs, or stopping what does not, is not an error.
/// </summary>
public sealed class ObsSink(ObsSettings settings, Action<IEventSink, Exception?>? later = null) : IEventSink
{
    public ObsSettings Settings => settings;

    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        var rules = settings.Rules.Where(r => r.Event == gameEvent.Type).ToList();
        foreach (var rule in rules.Where(r => r.DelaySeconds > 0))
            Later(TimeSpan.FromSeconds(rule.DelaySeconds), session => RunAsync(session, rule, gameEvent), rule.Action == "save_replay");
        var now = rules.Where(r => r.DelaySeconds <= 0).ToList();
        if (now.Count == 0) return;

        await using var session = await ObsSession.ConnectAsync(settings, now.Any(r => r.Action == "save_replay"), cancellationToken);
        var errors = new List<Exception>();
        foreach (var rule in now)
            try { await RunAsync(session, rule, gameEvent); }
            catch (Exception e) when (e is ObsException) { errors.Add(e); }
        if (errors.Count > 0) throw new ObsException(string.Join(" ", errors.Select(e => e.Message)));
    }

    /// <summary>Connects and asks for the OBS version; throws when OBS cannot be reached or rejects the password.</summary>
    public static async Task<string> TestAsync(ObsSettings settings, CancellationToken cancellationToken)
    {
        await using var session = await ObsSession.ConnectAsync(settings, false, cancellationToken);
        return (string?)(await session.RequestAsync("GetVersion"))?["obsVersion"] ?? "?";
    }

    async Task RunAsync(ObsSession obs, ObsRule rule, GameEvent e)
    {
        if (rule.Action is "switch_scene" or "set_text" or "show_source" && rule.Param is "")
            throw new ObsException($"OBS rule {rule.Action} has no scene or source name.");
        switch (rule.Action)
        {
            case "start_recording": await obs.RequestAsync("StartRecord", alreadyDone: true); break;
            case "stop_recording": await obs.RequestAsync("StopRecord", alreadyDone: true); break;
            case "pause_recording": await obs.RequestAsync("PauseRecord", alreadyDone: true); break;
            case "resume_recording": await obs.RequestAsync("ResumeRecord", alreadyDone: true); break;
            case "split_recording": await obs.RequestAsync("SplitRecordFile"); break;
            case "record_chapter":
                await obs.RequestAsync("CreateRecordChapter", new JsonObject { ["chapterName"] = ObsTemplate.Fill(rule.Param is "" ? "Kill {kills}" : rule.Param, e) });
                break;
            case "start_replay_buffer": await obs.RequestAsync("StartReplayBuffer", alreadyDone: true); break;
            case "stop_replay_buffer": await obs.RequestAsync("StopReplayBuffer", alreadyDone: true); break;
            case "save_replay":
                await obs.RequestAsync("SaveReplayBuffer");
                if (rule.Param is not "") RenameReplay(await obs.WaitForReplayAsync(), ObsTemplate.Fill(rule.Param, e));
                break;
            case "switch_scene": await obs.RequestAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = rule.Param }); break;
            case "set_text":
                await obs.RequestAsync("SetInputSettings", new JsonObject
                {
                    ["inputName"] = rule.Param,
                    ["inputSettings"] = new JsonObject { ["text"] = ObsTemplate.Fill(rule.Text, e) },
                });
                break;
            case "show_source":
                var (scene, item) = await SceneItemAsync(obs, rule.Param);
                await SetEnabledAsync(obs, scene, item, true);
                if (rule.DurationSeconds > 0)
                    Later(TimeSpan.FromSeconds(rule.DurationSeconds), hide => SetEnabledAsync(hide, scene, item, false), false);
                break;
            case "start_streaming": await obs.RequestAsync("StartStream", alreadyDone: true); break;
            case "stop_streaming": await obs.RequestAsync("StopStream", alreadyDone: true); break;
            default: throw new ObsException($"Unknown OBS action {rule.Action}.");
        }
    }

    static async Task<(string Scene, int Item)> SceneItemAsync(ObsSession obs, string source)
    {
        var scene = (string)(await obs.RequestAsync("GetCurrentProgramScene"))!["currentProgramSceneName"]!;
        var item = (int)(await obs.RequestAsync("GetSceneItemId", new JsonObject { ["sceneName"] = scene, ["sourceName"] = source }))!["sceneItemId"]!;
        return (scene, item);
    }

    static Task SetEnabledAsync(ObsSession obs, string scene, int item, bool enabled) =>
        obs.RequestAsync("SetSceneItemEnabled", new JsonObject { ["sceneName"] = scene, ["sceneItemId"] = item, ["sceneItemEnabled"] = enabled });

    void Later(TimeSpan delay, Func<ObsSession, Task> action, bool replayEvents) => _ = Task.Run(async () =>
    {
        await Task.Delay(delay);
        try
        {
            await using var session = await ObsSession.ConnectAsync(settings, replayEvents, CancellationToken.None);
            await action(session);
            later?.Invoke(this, null);
        }
        catch (Exception e)
        {
            later?.Invoke(this, e);
        }
    });

    /// <summary>Renames the saved replay next to itself. Only possible when OBS runs on this PC.</summary>
    void RenameReplay(string path, string name)
    {
        if (!File.Exists(path)) throw new ObsException($"Saved replay {path} is not on this PC, so it cannot be renamed.");
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        var target = Path.Combine(Path.GetDirectoryName(path)!, safe + Path.GetExtension(path));
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(Path.GetDirectoryName(path)!, $"{safe}-{n}{Path.GetExtension(path)}");
        File.Move(path, target);
    }

    public override string ToString() => $"OBS {settings.Host}:{settings.Port}";
}

/// <summary>Placeholders in OBS texts, chapter names and replay file names.</summary>
public static class ObsTemplate
{
    /// <summary>{kills} the Player's kills so far in the Match (outside a Match: since the last one; from <c>data.kills</c> of
    /// kill and match_ended, 0 otherwise), {n} the event's sequence,
    /// {match} the first 8 characters of the matchId (or "mp"), {result} of a match_ended, {date} and {time} of the event.</summary>
    public static string Fill(string template, GameEvent e) => template
        .Replace("{kills}", Kills(e))
        .Replace("{n}", e.Sequence.ToString(CultureInfo.InvariantCulture))
        .Replace("{match}", e.MatchId is { } id ? id[..Math.Min(8, id.Length)] : "mp")
        .Replace("{result}", e.Data.GetValueOrDefault("result", ""))
        .Replace("{date}", e.OccurredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        .Replace("{time}", e.OccurredAt.ToString("HH-mm-ss", CultureInfo.InvariantCulture));

    static string Kills(GameEvent e) => e.Data.GetValueOrDefault("kills", "0");
}

public sealed class ObsException(string message) : Exception(message);

/// <summary>A minimal OBS WebSocket v5 session: Hello, Identify (with the password if OBS asks), then requests in order.</summary>
public sealed class ObsSession : IAsyncDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5), ReplayTimeout = TimeSpan.FromSeconds(15);
    const int OutputEvents = 1 << 6;
    const int OutputRunning = 500, OutputNotRunning = 501, OutputPaused = 502, OutputNotPaused = 503;

    readonly ClientWebSocket _socket = new();
    readonly Queue<JsonNode> _events = new();
    readonly CancellationToken _cancel;

    ObsSession(CancellationToken cancel) => _cancel = cancel;

    /// <param name="outputEvents">Subscribe to output events (needed to learn the file of a saved replay).</param>
    public static async Task<ObsSession> ConnectAsync(ObsSettings settings, bool outputEvents, CancellationToken cancellationToken)
    {
        var session = new ObsSession(cancellationToken);
        try
        {
            using var timeout = session.Deadline(Timeout);
            session._socket.Options.AddSubProtocol("obswebsocket.json");
            await session._socket.ConnectAsync(new Uri($"ws://{settings.Host}:{settings.Port}"), timeout.Token);
            var hello = await session.ReceiveAsync(timeout.Token);
            var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = outputEvents ? OutputEvents : 0 };
            if (hello["d"]?["authentication"] is { } auth)
                identify["authentication"] = Authentication(settings.Password, (string)auth["salt"]!, (string)auth["challenge"]!);
            await session.SendAsync(1, identify, timeout.Token);
            if ((int?)(await session.ReceiveAsync(timeout.Token))["op"] != 2) throw new ObsException("OBS did not accept the connection.");
            return session;
        }
        catch
        {
            session._socket.Dispose();
            throw;
        }
    }

    /// <summary>Sends one request and returns its response data. Throws <see cref="ObsException"/> when it fails.</summary>
    /// <param name="alreadyDone">OBS answering "already running / not running / paused / not paused" counts as done.</param>
    public async Task<JsonNode?> RequestAsync(string type, JsonObject? data = null, bool alreadyDone = false)
    {
        using var timeout = Deadline(Timeout);
        var id = Guid.NewGuid().ToString("N");
        await SendAsync(6, new JsonObject { ["requestType"] = type, ["requestId"] = id, ["requestData"] = data }, timeout.Token);
        while (true)
        {
            var message = await ReceiveAsync(timeout.Token);
            if ((int?)message["op"] == 5) { _events.Enqueue(message["d"]!); continue; }
            if ((int?)message["op"] != 7 || (string?)message["d"]?["requestId"] != id) continue;
            var status = message["d"]!["requestStatus"]!;
            var code = (int?)status["code"] ?? 0;
            if ((bool?)status["result"] == true || alreadyDone && code is OutputRunning or OutputNotRunning or OutputPaused or OutputNotPaused)
                return message["d"]!["responseData"];
            throw new ObsException($"OBS {type} failed: {(string?)status["comment"] ?? "code " + code}.");
        }
    }

    /// <summary>Waits for OBS to report the saved replay file (needs <c>outputEvents</c>).</summary>
    public async Task<string> WaitForReplayAsync()
    {
        using var timeout = Deadline(ReplayTimeout);
        while (true)
        {
            var e = _events.Count > 0 ? _events.Dequeue() : (await ReceiveAsync(timeout.Token)) is { } m && (int?)m["op"] == 5 ? m["d"]! : null;
            if ((string?)e?["eventType"] == "ReplayBufferSaved") return (string)e!["eventData"]!["savedReplayPath"]!;
        }
    }

    /// <summary>OBS WebSocket v5: base64(sha256(base64(sha256(password + salt)) + challenge)).</summary>
    public static string Authentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    CancellationTokenSource Deadline(TimeSpan after)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancel);
        cts.CancelAfter(after);
        return cts;
    }

    Task SendAsync(int op, JsonObject data, CancellationToken ct) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(new JsonObject { ["op"] = op, ["d"] = data }.ToJsonString()), WebSocketMessageType.Text, true, ct);

    async Task<JsonNode> ReceiveAsync(CancellationToken ct)
    {
        using var message = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new ObsException(_socket.CloseStatus == (WebSocketCloseStatus)4009 ? "OBS rejected the password." : $"OBS closed the connection: {_socket.CloseStatusDescription}");
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return JsonNode.Parse(message.ToArray())!;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = Deadline(TimeSpan.FromSeconds(1));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
        }
        catch (Exception) { }
        _socket.Dispose();
    }
}
