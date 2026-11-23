using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

/// <summary>
/// The state behind the main window (<c>ui/</c>, see its README for the messages): Game Events, Match, Event Sink
/// health, log, diagnostics, kill pictures. It lives as long as the app, so the window shows the history when it opens.
/// Host → UI messages go out through <see cref="Post"/>; UI → host messages come in through <see cref="Receive"/>.
/// All members are thread-safe; <see cref="Post"/> and <see cref="Problem"/> are raised on the calling thread.
/// </summary>
public sealed class UiHub : IEventSink
{
    const int MaxEvents = 500, MaxLog = 1000;
    /// <summary>Serves the data folder to the window: <c>diagnostics/</c> and <c>kills/</c>.</summary>
    public const string ImageHost = "matchpulse.local";

    readonly Func<Config> _config;
    readonly Action<Config> _apply;
    readonly Func<Config, IEnumerable<IEventSink>> _sinks;
    readonly string _dataFolder, _diagnosticsFolder, _killsFolder;
    readonly Lock _lock = new();
    readonly List<JsonNode> _events = [];
    readonly List<JsonNode> _logLines = [];
    readonly Dictionary<string, SinkHealth> _health = [];
    readonly HashSet<string> _problems = [];
    bool _game;
    JsonObject? _match;
    readonly Dictionary<string, (string Status, string? Error)> _plugins = new() { ["obs"] = ("disconnected", null), ["mqtt"] = ("disconnected", null), ["homeassistant"] = ("disconnected", null),
        ["wled"] = ("disconnected", null), ["sound"] = ("ready", null) };
    static readonly HttpClient PluginHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    sealed record SinkHealth(DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, string? Code, string? Message);

    /// <param name="config">The current settings.</param>
    /// <param name="apply">Saves and applies new settings; throws <see cref="InvalidDataException"/> when they are invalid.</param>
    /// <param name="sinks">Builds the Event Sinks of a config (used for test events).</param>
    /// <param name="dataFolder">Holds <c>diagnostics/</c> and <c>kills/</c>, served under <see cref="ImageHost"/>.</param>
    public UiHub(Func<Config> config, Action<Config> apply, Func<Config, IEnumerable<IEventSink>> sinks, string dataFolder)
    {
        _config = config;
        _apply = apply;
        _sinks = sinks;
        _dataFolder = dataFolder;
        _diagnosticsFolder = Path.Combine(dataFolder, "diagnostics");
        _killsFolder = Path.Combine(dataFolder, "kills");
    }

    public string DataFolder => _dataFolder;

    /// <summary>Auto-update; set once by the tray app (null in tests and the terminal modes).</summary>
    public UpdateService? Updates
    {
        get => _updates;
        set
        {
            _updates = value;
            if (value is not null) value.Changed += () => Send("update", value.State());
        }
    }
    UpdateService? _updates;

    /// <summary>A message for the window: type and payload.</summary>
    public event Action<string, JsonNode?>? Post;

    /// <summary>The window asks for a page in a window of its own (the live monitor).</summary>
    public event Action<string>? WindowRequested;

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
        if (sink is ObsSink) { PluginResult("obs", error); return; }
        if (sink is WledSink) { PluginResult("wled", error); return; }
        if (sink is SoundSink) { PluginResult("sound", error); return; }
        if (sink is MqttSink mqtt)
        {
            PluginResult("mqtt", error);
            if (_config().HomeAssistant.Enabled) PluginResult("homeassistant", error);
            return;
        }
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

    /// <summary>Settings changed outside the window (the hotkey); the window shows them.</summary>
    public void SettingsChanged() => Send("settings", Settings(_config()));

    /// <summary>A kill picture was saved (live monitor).</summary>
    public void KillPicture(GameEvent kill, string path) =>
        Send("killHighlight", Highlight(Path.GetFileName(path)));

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
            case "savePlugin" when (string?)payload!["id"] == "obs": Apply(_config() with { Obs = ObsFrom(payload!.AsObject()) }); break;
            case "testPlugin" when (string?)payload!["id"] == "obs": _ = TestObsAsync(_config().Obs); break;
            case "savePlugin" when (string?)payload!["id"] == "mqtt": Apply(_config() with { Mqtt = MqttFrom(payload!.AsObject()) }); break;
            case "testPlugin" when (string?)payload!["id"] == "mqtt": _ = TestMqttAsync(_config().Mqtt); break;
            case "savePlugin" when (string?)payload!["id"] == "homeassistant": Apply(_config() with { HomeAssistant = HomeAssistantFrom(payload!.AsObject()) }); break;
            case "testPlugin" when (string?)payload!["id"] == "homeassistant": _ = TestHomeAssistantAsync(_config()); break;
            case "savePlugin" when (string?)payload!["id"] == "wled": Apply(_config() with { Wled = WledFrom(payload!.AsObject()) }); break;
            case "testPlugin" when (string?)payload!["id"] == "wled": _ = TestWledAsync(_config().Wled); break;
            case "savePlugin" when (string?)payload!["id"] == "sound": Apply(_config() with { Sound = SoundFrom(payload!.AsObject()) }); break;
            case "testRule": _ = TestRuleAsync((string?)payload!["id"], (string?)payload["ruleId"]); break;
            case "setActive": Apply(_config() with { Active = (bool?)payload!["active"] ?? true }); break;
            case "setLiveMonitor": Apply(_config() with { LiveMonitor = (bool?)payload!["enabled"] ?? false }); break;
            case "openLiveWindow": WindowRequested?.Invoke("live"); break;
            case "checkUpdates" when _updates is not null: _ = _updates.CheckAsync(); break;
            case "installUpdate" when _updates is not null: _ = _updates.InstallAsync(); break;
            case "openReleasePage" when (string?)_updates?.State()["releaseUrl"] is { } url:
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                break;
            // The audio plugin is not built yet; only OBS is listed.
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
                ["plugins"] = new JsonArray(Obs(config.Obs), Mqtt(config.Mqtt), HomeAssistantPlugin(config), Wled(config.Wled), SoundPlugin(config.Sound)),
                ["settings"] = Settings(config),
                ["highlights"] = Highlights(),
                ["diagnostics"] = Diagnostics(),
                ["log"] = new JsonArray([.. _logLines.Select(l => l.DeepClone())]),
                ["update"] = _updates?.State() ?? new JsonObject { ["current"] = Updater.AppVersion.ToString(), ["status"] = "idle", ["installable"] = false },
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

    // ---------- OBS plugin ----------

    async Task TestObsAsync(ObsSettings settings)
    {
        try
        {
            var version = await ObsSink.TestAsync(settings, CancellationToken.None);
            Logged(DateTimeOffset.Now, LogLevel.Information, $"OBS {version} reached at {settings.Host}:{settings.Port}");
            PluginResult("obs", null);
        }
        catch (Exception e)
        {
            Logged(DateTimeOffset.Now, LogLevel.Warning, $"OBS test at {settings.Host}:{settings.Port} failed: {e.Message}");
            PluginResult("obs", e);
        }
    }

    async Task TestMqttAsync(MqttSettings settings)
    {
        try
        {
            await MqttSink.TestAsync(settings, CancellationToken.None);
            Logged(DateTimeOffset.Now, LogLevel.Information, $"MQTT broker reached at {settings.Host}:{settings.Port}");
            PluginResult("mqtt", null);
        }
        catch (Exception e)
        {
            Logged(DateTimeOffset.Now, LogLevel.Warning, $"MQTT test at {settings.Host}:{settings.Port} failed: {e.Message}");
            PluginResult("mqtt", e);
        }
    }

    async Task TestHomeAssistantAsync(Config config)
    {
        try
        {
            if (!config.Mqtt.Enabled) throw new MqttRefusedException("Home Assistant needs the MQTT plugin: turn it on and enter the broker first.");
            await MqttSink.TestAsync(config.Mqtt, CancellationToken.None, config.HomeAssistant with { Enabled = true });
            Logged(DateTimeOffset.Now, LogLevel.Information, $"Home Assistant discovery published under {config.HomeAssistant.DiscoveryPrefix}/");
            PluginResult("homeassistant", null);
        }
        catch (Exception e)
        {
            Logged(DateTimeOffset.Now, LogLevel.Warning, $"Home Assistant discovery failed: {e.Message}");
            PluginResult("homeassistant", e);
        }
    }

    JsonObject HomeAssistantPlugin(Config config)
    {
        lock (_lock)
            return new JsonObject
            {
                ["id"] = "homeassistant",
                ["enabled"] = config.HomeAssistant.Enabled,
                ["status"] = !config.HomeAssistant.Enabled ? "disconnected" : !config.Mqtt.Enabled ? "error" : _plugins["homeassistant"].Status,
                ["lastError"] = config.HomeAssistant.Enabled && !config.Mqtt.Enabled ? "Home Assistant needs the MQTT plugin: turn it on and enter the broker first." : _plugins["homeassistant"].Error,
                ["config"] = new JsonObject
                {
                    ["discoveryPrefix"] = config.HomeAssistant.DiscoveryPrefix,
                    ["topicPrefix"] = config.Mqtt.TopicPrefix,
                    ["mqttEnabled"] = config.Mqtt.Enabled,
                },
                ["rules"] = new JsonArray(),
            };
    }

    public static HomeAssistantSettings HomeAssistantFrom(JsonObject p) =>
        new((bool?)p["enabled"] ?? false, ((string?)p["config"]?["discoveryPrefix"] ?? "").Trim());

    // ---------- WLED and sound plugins ----------

    async Task TestWledAsync(WledSettings settings)
    {
        try
        {
            var name = await WledSink.TestAsync(PluginHttp, settings, CancellationToken.None);
            Logged(DateTimeOffset.Now, LogLevel.Information, $"WLED {name} reached at {settings.Host}");
            PluginResult("wled", null);
        }
        catch (Exception e)
        {
            Logged(DateTimeOffset.Now, LogLevel.Warning, $"WLED test at {settings.Host} failed: {e.Message}");
            PluginResult("wled", e);
        }
    }

    /// <summary>The plugin page's play button: runs one rule now, whatever its event.</summary>
    async Task TestRuleAsync(string? plugin, string? ruleId)
    {
        var config = _config();
        try
        {
            if (plugin == "sound" && config.Sound.Rules.FirstOrDefault(r => r.Id == ruleId) is { } sound)
                SoundSink.Play(sound.Sound, Math.Clamp(sound.Volume, 0, 100) / 100f);
            else if (plugin == "wled" && config.Wled.Rules.FirstOrDefault(r => r.Id == ruleId) is { } wled)
                await new WledSink(PluginHttp, config.Wled, (_, e) => PluginResult("wled", e)).RunAsync(wled, CancellationToken.None);
            else return;
            PluginResult(plugin, null);
        }
        catch (Exception e)
        {
            Logged(DateTimeOffset.Now, LogLevel.Warning, $"{plugin} rule test failed: {e.Message}");
            PluginResult(plugin, e);
        }
    }

    JsonObject Wled(WledSettings w)
    {
        lock (_lock)
            return new JsonObject
            {
                ["id"] = "wled",
                ["enabled"] = w.Enabled,
                ["status"] = _plugins["wled"].Status,
                ["lastError"] = _plugins["wled"].Error,
                ["config"] = new JsonObject { ["host"] = w.Host },
                ["rules"] = new JsonArray([.. w.Rules.Select(r => (JsonNode)new JsonObject
                {
                    ["id"] = r.Id, ["event"] = r.Event, ["action"] = r.Action, ["color"] = r.Color,
                    ["effect"] = r.Effect, ["durationSeconds"] = r.DurationSeconds, ["preset"] = r.Preset,
                })]),
            };
    }

    JsonObject SoundPlugin(SoundSettings s)
    {
        lock (_lock)
            return new JsonObject
            {
                ["id"] = "sound",
                ["enabled"] = s.Enabled,
                ["status"] = _plugins["sound"].Status,
                ["lastError"] = _plugins["sound"].Error,
                ["config"] = new JsonObject(),
                ["rules"] = new JsonArray([.. s.Rules.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["event"] = r.Event, ["sound"] = r.Sound, ["volume"] = r.Volume })]),
            };
    }

    public static WledSettings WledFrom(JsonObject p) => new(
        (bool?)p["enabled"] ?? false,
        ((string?)p["config"]?["host"] ?? "").Trim(),
        [.. p["rules"]!.AsArray().Select(r => new WledRule(
            (string?)r!["id"] ?? Guid.NewGuid().ToString("N"), (string?)r["event"] ?? "", (string?)r["action"] ?? "",
            ((string?)r["color"] ?? "#FF0000").ToUpperInvariant(), (string?)r["effect"] ?? "blink",
            (double?)r["durationSeconds"] ?? 1.5, (int?)r["preset"] ?? 1))]);

    public static SoundSettings SoundFrom(JsonObject p) => new(
        (bool?)p["enabled"] ?? false,
        [.. p["rules"]!.AsArray().Select(r => new SoundRule(
            (string?)r!["id"] ?? Guid.NewGuid().ToString("N"), (string?)r["event"] ?? "", ((string?)r["sound"] ?? "ding").Trim().Trim('"'), (int?)r["volume"] ?? 80))]);

    void PluginResult(string id, Exception? error)
    {
        bool newFailure;
        (string Status, string? Error) state;
        lock (_lock)
        {
            newFailure = error is not null && _plugins[id].Status != "error";
            state = _plugins[id] = (error is null ? (id == "sound" ? "ready" : "connected") : "error", error is null ? null : PluginMessage(id, error));
        }
        Send("pluginStatus", new JsonObject { ["id"] = id, ["status"] = state.Status, ["lastError"] = state.Error });
        if (newFailure && id != "homeassistant")
            Problem?.Invoke(id switch { "obs" => "ObsFailed", "mqtt" => "MqttFailed", "wled" => "WledFailed", _ => "SoundFailed" }, "plugins");
    }

    static string PluginMessage(string id, Exception e) => e switch
    {
        ObsException or MqttRefusedException => e.Message,
        WebSocketException or OperationCanceledException or System.Net.Sockets.SocketException or HttpRequestException => id switch
        {
            "obs" => "OBS is not reachable.",
            "wled" => "The WLED controller is not reachable.",
            _ => "The MQTT broker is not reachable.",
        },
        _ when e.InnerException is System.Net.Sockets.SocketException => "The MQTT broker is not reachable.",
        _ => e.Message,
    };

    JsonObject Mqtt(MqttSettings m)
    {
        lock (_lock)
            return new JsonObject
            {
                ["id"] = "mqtt",
                ["enabled"] = m.Enabled,
                ["status"] = _plugins["mqtt"].Status,
                ["lastError"] = _plugins["mqtt"].Error,
                ["config"] = new JsonObject
                {
                    ["host"] = m.Host, ["port"] = m.Port, ["useTls"] = m.UseTls, ["username"] = m.Username,
                    ["password"] = m.Password, ["topicPrefix"] = m.TopicPrefix, ["qos"] = m.Qos,
                },
                ["rules"] = new JsonArray(),
            };
    }

    public static MqttSettings MqttFrom(JsonObject p)
    {
        var c = p["config"]!;
        return new MqttSettings(
            (bool?)p["enabled"] ?? false,
            ((string?)c["host"] ?? "").Trim(),
            (int?)c["port"] ?? 0,
            (bool?)c["useTls"] ?? false,
            ((string?)c["username"] ?? "").Trim(),
            (string?)c["password"] ?? "",
            ((string?)c["topicPrefix"] ?? "").Trim(),
            (int?)c["qos"] ?? 1);
    }

    JsonObject Obs(ObsSettings obs)
    {
        lock (_lock)
            return new JsonObject
            {
                ["id"] = "obs",
                ["enabled"] = obs.Enabled,
                ["status"] = _plugins["obs"].Status,
                ["lastError"] = _plugins["obs"].Error,
                ["config"] = new JsonObject { ["host"] = obs.Host, ["port"] = obs.Port, ["password"] = obs.Password },
                ["rules"] = new JsonArray([.. obs.Rules.Select(r => (JsonNode)new JsonObject
                {
                    ["id"] = r.Id, ["event"] = r.Event, ["action"] = r.Action, ["param"] = r.Param,
                    ["text"] = r.Text, ["delaySeconds"] = r.DelaySeconds, ["durationSeconds"] = r.DurationSeconds,
                })]),
            };
    }

    public static ObsSettings ObsFrom(JsonObject p)
    {
        var c = p["config"]!;
        return new ObsSettings(
            (bool?)p["enabled"] ?? false,
            ((string?)c["host"] ?? "").Trim(),
            (int?)c["port"] ?? 0,
            (string?)c["password"] ?? "",
            [.. p["rules"]!.AsArray().Select(r => new ObsRule(
                (string?)r!["id"] ?? Guid.NewGuid().ToString("N"), (string?)r["event"] ?? "", (string?)r["action"] ?? "",
                ((string?)r["param"] ?? "").Trim(), (string?)r["text"] ?? "", (double?)r["delaySeconds"] ?? 0, (double?)r["durationSeconds"] ?? 0))]);
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
        ["liveMonitor"] = config.LiveMonitor,
        ["active"] = config.Active,
        ["toggleHotkey"] = config.ToggleHotkey,
        ["checkForUpdates"] = config.CheckForUpdates,
        ["autoInstallUpdates"] = config.AutoInstallUpdates,
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
        LiveMonitor = (bool?)s["liveMonitor"] ?? config.LiveMonitor,
        Active = (bool?)s["active"] ?? config.Active,
        ToggleHotkey = ((string?)s["toggleHotkey"] ?? config.ToggleHotkey).Trim(),
        CheckForUpdates = (bool?)s["checkForUpdates"] ?? config.CheckForUpdates,
        AutoInstallUpdates = (bool?)s["autoInstallUpdates"] ?? config.AutoInstallUpdates,
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
            ["imageUrl"] = $"https://{ImageHost}/diagnostics/{Uri.EscapeDataString(name)}",
        };
    })]);

    /// <summary>Kill pictures, oldest first.</summary>
    public JsonArray Highlights() => new([..
        (Directory.Exists(_killsFolder) ? Directory.EnumerateFiles(_killsFolder, "*.jpg") : [])
            .Select(Path.GetFileName).Order().Select(name => Highlight(name!)).OfType<JsonNode>()]);

    static JsonObject? Highlight(string name) => KillPictures.Parse(name) is { } p
        ? new JsonObject
        {
            ["matchId"] = p.MatchId,
            ["sequence"] = p.Sequence,
            ["capturedAt"] = p.At,
            ["imageUrl"] = $"https://{ImageHost}/kills/{Uri.EscapeDataString(name)}",
        }
        : null;

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    void Send(string type, JsonNode? payload) => Post?.Invoke(type, payload);
}
