using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

/// <summary>
/// The state behind the main window (<c>ui/</c>, see its README for the messages): Game Events, Match, Event Sink
/// health, log, diagnostics. It lives as long as the app, so the window shows the history when it opens.
/// Host → UI messages go out through <see cref="Post"/>; UI → host messages come in through <see cref="Receive"/>.
/// All members are thread-safe; <see cref="Post"/> and <see cref="Problem"/> are raised on the calling thread.
/// </summary>
public sealed class UiHub : IEventSink
{
    const int MaxEvents = 500, MaxLog = 1000;
    public const string ImageHost = "matchpulse.local";

    readonly Func<Config> _config;
    readonly Action<Config> _apply;
    readonly Func<Config, IEnumerable<IEventSink>> _sinks;
    readonly string _diagnosticsFolder;
    readonly Lock _lock = new();
    readonly List<JsonNode> _events = [];
    readonly List<JsonNode> _logLines = [];
    readonly Dictionary<string, SinkHealth> _health = [];
    readonly HashSet<string> _problems = [];
    bool _game;
    JsonObject? _match;

    sealed record SinkHealth(DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, string? Code, string? Message);

    /// <param name="config">The current settings.</param>
    /// <param name="apply">Saves and applies new settings; throws <see cref="InvalidDataException"/> when they are invalid.</param>
    /// <param name="sinks">Builds the Event Sinks of a config (used for test events).</param>
    public UiHub(Func<Config> config, Action<Config> apply, Func<Config, IEnumerable<IEventSink>> sinks, string diagnosticsFolder)
    {
        _config = config;
        _apply = apply;
        _sinks = sinks;
        _diagnosticsFolder = diagnosticsFolder;
    }

    /// <summary>A message for the window: type and payload.</summary>
    public event Action<string, JsonNode?>? Post;

    /// <summary>A problem the Player should be told about outside the window: problem key and the page that shows it.</summary>
    public event Action<string, string>? Problem;

    public static string? SinkId(IEventSink sink) => sink switch
    {
        WebhookSink w => "webhook:" + w.Url,
        ScriptSink s => "script:" + s.Command,
        _ => null,
    };

    // ---------- Host side ----------

    public void SetGameRunning(bool running)
    {
        lock (_lock)
        {
            _game = running;
            if (!running) _match = null;
            _problems.Remove("capture_failed");
        }
        Send("state", State());
        Send("hostProblems", Problems());
    }

    /// <summary>The config file could not be read; defaults apply.</summary>
    public void ConfigInvalid()
    {
        lock (_lock) _problems.Add("config_invalid");
        Send("hostProblems", Problems());
    }

    /// <summary>As an Event Sink: records each Game Event and follows the Match.</summary>
    public Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        var node = JsonNode.Parse(gameEvent.ToJson())!;
        lock (_lock)
        {
            _events.Add(node.DeepClone());
            if (_events.Count > MaxEvents) _events.RemoveAt(0);
            if (gameEvent.Type == "match_started")
                _match = new JsonObject { ["matchId"] = gameEvent.MatchId, ["startedAt"] = gameEvent.OccurredAt };
            else if (gameEvent.Type == "match_ended")
                _match = null;
        }
        Send("gameEvent", node);
        if (gameEvent.Type is "match_started" or "match_ended") Send("state", State());
        return Task.CompletedTask;
    }

    /// <summary>Records the result of a delivery to an Event Sink (from <see cref="EventDispatcher.Delivered"/>).</summary>
    public void Delivered(IEventSink sink, Exception? error)
    {
        if (SinkId(sink) is not { } id) return;
        bool newFailure;
        lock (_lock)
        {
            var before = _health.GetValueOrDefault(id);
            var now = DateTimeOffset.Now;
            _health[id] = error is null
                ? new SinkHealth(now, before?.LastFailureAt, null, null)
                : new SinkHealth(before?.LastSuccessAt, now, ErrorCode(error), error.Message);
            newFailure = error is not null && before?.Code is null;
        }
        Send("sinks", Sinks());
        if (newFailure) Problem?.Invoke("SinkFailed", "sinks");
    }

    /// <summary>A log line (from <see cref="FileLog.Written"/>).</summary>
    public void Logged(DateTimeOffset at, LogLevel level, string message)
    {
        var lvl = level >= LogLevel.Error ? "ERROR" : level == LogLevel.Warning ? "WARN" : message.StartsWith("Game Event ", StringComparison.Ordinal) ? "EVENT" : "INFO";
        var line = new JsonObject { ["at"] = at, ["level"] = lvl, ["message"] = message };
        lock (_lock)
        {
            _logLines.Add(line.DeepClone());
            if (_logLines.Count > MaxLog) _logLines.RemoveAt(0);
        }
        Send("log", new JsonArray(line));

        if (message.StartsWith("Capture of monitor", StringComparison.Ordinal))
        {
            bool added;
            lock (_lock) added = _problems.Add("capture_failed");
            if (added)
            {
                Send("hostProblems", Problems());
                Problem?.Invoke("CaptureFailed", "log");
            }
        }
        if (message.StartsWith("Diagnostic snapshot ", StringComparison.Ordinal)) Send("diagnostics", Diagnostics());
    }

    // ---------- UI side ----------

    /// <summary>A message from the window, as JSON <c>{ "type": …, "payload": … }</c>.</summary>
    public void Receive(string json)
    {
        var message = JsonNode.Parse(json)!.AsObject();
        var payload = message["payload"];
        switch ((string?)message["type"])
        {
            case "ready": Send("snapshot", Snapshot()); break;
            case "saveSettings": Apply(SettingsFrom(_config(), payload!.AsObject())); break;
            case "saveSinks": Apply(SinksFrom(_config(), payload!.AsArray())); break;
            case "sendTestEvent": SendTestEvent(payload!["sinkIds"]!.AsArray().Select(n => (string)n!).ToHashSet()); break;
            case "openDiagnosticsFolder":
                Directory.CreateDirectory(_diagnosticsFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_diagnosticsFolder}\"") { UseShellExecute = false });
                break;
            case "clearDiagnostics":
                foreach (var file in DiagnosticFiles()) TryDelete(file);
                Send("diagnostics", Diagnostics());
                break;
            // Live monitor and plugins are not built yet; their pages are hidden.
        }
    }

    public JsonObject Snapshot()
    {
        var config = _config();
        lock (_lock)
            return new JsonObject
            {
                ["appState"] = AppState(),
                ["match"] = _match?.DeepClone(),
                ["hostProblems"] = Problems(),
                ["events"] = new JsonArray([.. _events.Select(e => e.DeepClone())]),
                ["sinks"] = Sinks(config),
                ["plugins"] = new JsonArray(),
                ["settings"] = Settings(config),
                ["highlights"] = new JsonArray(),
                ["diagnostics"] = Diagnostics(),
                ["log"] = new JsonArray([.. _logLines.Select(l => l.DeepClone())]),
            };
    }

    void Apply(Config config)
    {
        try
        {
            _apply(config);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Logged(DateTimeOffset.Now, LogLevel.Error, $"Settings not saved: {e.Message}");
        }
        // Echo what is in force now, so the window shows a rejected change reverted.
        Send("settings", Settings(_config()));
        Send("sinks", Sinks());
    }

    void SendTestEvent(HashSet<string> ids)
    {
        var test = new GameEvent("test", null, 0, DateTimeOffset.Now, new Dictionary<string, string>());
        foreach (var sink in _sinks(_config()).Where(s => SinkId(s) is { } id && ids.Contains(id)))
            _ = Task.Run(async () =>
            {
                try
                {
                    await sink.SendAsync(test, CancellationToken.None);
                    Logged(DateTimeOffset.Now, LogLevel.Information, $"Test event delivered to {sink}");
                    Delivered(sink, null);
                }
                catch (Exception e)
                {
                    Logged(DateTimeOffset.Now, LogLevel.Warning, $"Test event to {sink} failed: {e.Message}");
                    Delivered(sink, e);
                }
            });
    }

    // ---------- JSON shapes ----------

    JsonObject State()
    {
        lock (_lock) return new JsonObject { ["appState"] = AppState(), ["match"] = _match?.DeepClone() };
    }

    string AppState() => _match is not null ? "match" : _game ? "game" : "off";

    JsonArray Problems()
    {
        lock (_lock) return new JsonArray([.. _problems.Order().Select(p => JsonValue.Create(p))]);
    }

    JsonArray Sinks() => Sinks(_config());

    JsonArray Sinks(Config config)
    {
        lock (_lock)
            return new JsonArray([..
                config.Webhooks.Select(u => Sink("webhook", u.ToString())).Concat(config.Scripts.Select(s => Sink("script", s)))]);

        JsonObject Sink(string kind, string target)
        {
            var health = _health.GetValueOrDefault(kind + ":" + target);
            return new JsonObject
            {
                ["id"] = kind + ":" + target,
                ["kind"] = kind,
                ["target"] = target,
                ["lastSuccessAt"] = health?.LastSuccessAt,
                ["lastFailureAt"] = health?.LastFailureAt,
                ["lastError"] = health?.Code is null ? null : new JsonObject { ["code"] = health.Code, ["message"] = health.Message },
            };
        }
    }

    static JsonObject Settings(Config config) => new()
    {
        ["gameLanguage"] = config.GameLanguage,
        ["uiLanguage"] = config.UiLanguage,
        ["theme"] = config.Theme,
        ["killMarkerColor"] = config.KillMarkerColor.ToUpperInvariant(),
        ["checksPerSecond"] = config.ChecksPerSecond,
        ["matchEndWithoutHudSeconds"] = config.HudLostAfterSeconds,
        ["diagnosticMode"] = config.Diagnostics,
        ["notifyOnProblems"] = config.NotifyOnProblems,
        ["liveMonitor"] = false,
    };

    public static Config SettingsFrom(Config config, JsonObject s) => config with
    {
        GameLanguage = (string?)s["gameLanguage"] ?? config.GameLanguage,
        UiLanguage = (string?)s["uiLanguage"] ?? config.UiLanguage,
        Theme = (string?)s["theme"] ?? config.Theme,
        KillMarkerColor = (string?)s["killMarkerColor"] ?? config.KillMarkerColor,
        ChecksPerSecond = (double?)s["checksPerSecond"] ?? config.ChecksPerSecond,
        HudLostAfterSeconds = (double?)s["matchEndWithoutHudSeconds"] ?? config.HudLostAfterSeconds,
        Diagnostics = (bool?)s["diagnosticMode"] ?? config.Diagnostics,
        NotifyOnProblems = (bool?)s["notifyOnProblems"] ?? config.NotifyOnProblems,
    };

    /// <summary>The config with the window's list of Event Sinks. A malformed webhook URL makes the config invalid.</summary>
    public static Config SinksFrom(Config config, JsonArray sinks)
    {
        var list = sinks.Select(s => ((string?)s!["kind"], ((string?)s["target"] ?? "").Trim())).ToList();
        return config with
        {
            Webhooks = [.. list.Where(s => s.Item1 == "webhook").Select(s => Uri.TryCreate(s.Item2, UriKind.Absolute, out var u) ? u
                : throw new InvalidDataException($"Webhook {s.Item2} is not an absolute URL."))],
            Scripts = [.. list.Where(s => s.Item1 == "script").Select(s => s.Item2)],
        };
    }

    static string ErrorCode(Exception e) => e switch
    {
        TimeoutException => "script_timeout",
        TaskCanceledException => "timeout",
        HttpRequestException => "http",
        _ => "error",
    };

    // ---------- Diagnostics ----------

    IEnumerable<string> DiagnosticFiles() =>
        Directory.Exists(_diagnosticsFolder) ? Directory.EnumerateFiles(_diagnosticsFolder, "*.jpg") : [];

    /// <summary>Snapshots from <see cref="DiagnosticSnapshots"/>, newest first. File names start with the local time of the case.</summary>
    public JsonArray Diagnostics() => new([.. DiagnosticFiles().Select(Path.GetFileName).OrderDescending().Select(name =>
    {
        var at = DateTime.TryParseExact(name![..Math.Min(23, name.Length)], "yyyy-MM-dd_HH-mm-ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)
            ? new DateTimeOffset(t) : (DateTimeOffset)File.GetLastWriteTime(Path.Combine(_diagnosticsFolder, name));
        return (JsonNode)new JsonObject
        {
            ["id"] = name,
            ["capturedAt"] = at,
            ["kind"] = name.Contains("kill_badge_without_kill_marker") ? "skull_without_marker" : "marker_without_skull",
            ["imageUrl"] = $"https://{ImageHost}/{Uri.EscapeDataString(name)}",
        };
    })]);

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    void Send(string type, JsonNode? payload) => Post?.Invoke(type, payload);
}
