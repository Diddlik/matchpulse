using System.Text.Json;
using System.Text.RegularExpressions;

namespace MatchPulse;

/// <summary>
/// Local config file, for example
/// <c>{ "webhooks": ["http://localhost:8080/bf6"], "scripts": ["pwsh -File C:\obs.ps1"], "gameLanguage": "de", "uiLanguage": "en", "checksPerSecond": 10, "hudLostAfterSeconds": 120, "killMarkerColor": "#FF0000" }</c>.
/// <c>killMarkerColor</c> is the Kill Marker colour set in the game settings, as <c>#RRGGBB</c>.
/// <c>"liveMonitor": true</c> saves a full-screen picture of each kill (see <see cref="KillPictures"/>).
/// <c>"diagnostics": true</c> saves a snapshot of every unconfirmed Kill case (see <see cref="DiagnosticSnapshots"/>).
/// Each script is a command line run through <c>cmd.exe /s /c "command"</c> once per Game Event.
/// </summary>
public sealed record Config(
    IReadOnlyList<Uri> Webhooks,
    double ChecksPerSecond = 10,
    IReadOnlyList<string>? Scripts = null,
    string GameLanguage = "de",
    string UiLanguage = "en",
    double HudLostAfterSeconds = 120,
    string KillMarkerColor = "#FF0000",
    bool Diagnostics = false,
    string Theme = "system",
    bool NotifyOnProblems = true,
    bool LiveMonitor = false,
    ObsSettings? Obs = null,
    bool CheckForUpdates = true,
    bool AutoInstallUpdates = true,
    MqttSettings? Mqtt = null)
{
    public static readonly string DefaultPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatchPulse", "config.json");

    public static readonly string[] Languages = ["de", "en"];
    public static readonly string[] Themes = ["system", "light", "dark"];

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<string> Scripts { get; init; } = Scripts ?? [];

    public ObsSettings Obs { get; init; } = Obs ?? new ObsSettings();

    public MqttSettings Mqtt { get; init; } = Mqtt ?? new MqttSettings();

    public static Config LoadOrDefault(string path) => File.Exists(path) ? Load(path) : new Config([]);

    public static Config Load(string path)
    {
        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        config.Validate(path);
        return config;
    }

    public void Save(string path)
    {
        Validate(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    void Validate(string path)
    {
        if (Webhooks is null) throw new InvalidDataException($"{path}: \"webhooks\" is missing.");
        foreach (var url in Webhooks)
            if (url is null || !url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
                throw new InvalidDataException($"{path}: webhook {url?.ToString() ?? "null"} is not an absolute http(s) URL.");
        if (ChecksPerSecond is < 5 or > 10)
            throw new InvalidDataException($"{path}: \"checksPerSecond\" must be between 5 and 10.");
        if (Scripts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"{path}: a script is empty.");
        if (HudLostAfterSeconds <= 0) throw new InvalidDataException($"{path}: \"hudLostAfterSeconds\" must be positive.");
        if (!Languages.Contains(GameLanguage)) throw new InvalidDataException($"{path}: \"gameLanguage\" must be de or en.");
        if (!Themes.Contains(Theme)) throw new InvalidDataException($"{path}: \"theme\" must be system, light or dark.");
        if (!Languages.Contains(UiLanguage)) throw new InvalidDataException($"{path}: \"uiLanguage\" must be de or en.");
        if (Obs.Port is < 1 or > 65535) throw new InvalidDataException($"{path}: OBS port must be between 1 and 65535.");
        if (Obs.Rules.Any(r => r is null || !ObsSettings.Events.Contains(r.Event) || !ObsSettings.Actions.Contains(r.Action)))
            throw new InvalidDataException($"{path}: an OBS rule has an unknown event or action.");
        if (Obs.Rules.Any(r => r.DelaySeconds is < 0 or > 60 || r.DurationSeconds is < 0 or > 60))
            throw new InvalidDataException($"{path}: OBS rule delays and durations must be between 0 and 60 seconds.");
        if (Mqtt.Port is < 1 or > 65535) throw new InvalidDataException($"{path}: MQTT port must be between 1 and 65535.");
        if (Mqtt.Qos is < 0 or > 2) throw new InvalidDataException($"{path}: MQTT QoS must be 0, 1 or 2.");
        if (string.IsNullOrWhiteSpace(Mqtt.TopicPrefix) || Mqtt.TopicPrefix.IndexOfAny(['#', '+']) >= 0 || Mqtt.TopicPrefix.EndsWith('/'))
            throw new InvalidDataException($"{path}: the MQTT topic prefix must not be empty, contain # or +, or end with /.");
        if (KillMarkerColor is null || !Regex.IsMatch(KillMarkerColor, "^#[0-9A-Fa-f]{6}$"))
            throw new InvalidDataException($"{path}: \"killMarkerColor\" must be a colour like #FF0000.");
    }
}

/// <summary>OBS plugin: OBS WebSocket (v5) connection and the rules "when Game Event, do action".
/// The password is stored as typed, like OBS stores it in its own settings.</summary>
public sealed record ObsSettings(bool Enabled = false, string Host = "localhost", int Port = 4455, string Password = "", IReadOnlyList<ObsRule>? Rules = null)
{
    public static readonly string[] Events = ["match_started", "kill", "knockdown", "death", "match_ended"];
    public static readonly string[] Actions =
    [
        "start_recording", "stop_recording", "pause_recording", "resume_recording", "split_recording", "record_chapter",
        "start_replay_buffer", "stop_replay_buffer", "save_replay", "switch_scene", "set_text", "show_source", "start_streaming", "stop_streaming",
    ];

    public IReadOnlyList<ObsRule> Rules { get; init; } = Rules ?? [];
}

/// <summary>When <see cref="Event"/> happens, run <see cref="Action"/> after <see cref="DelaySeconds"/>.
/// <see cref="Param"/>: scene (<c>switch_scene</c>), source (<c>set_text</c>, <c>show_source</c>), chapter name (<c>record_chapter</c>)
/// or replay file name (<c>save_replay</c>, empty keeps the OBS name). <see cref="Text"/>: the text of <c>set_text</c>.
/// <see cref="DurationSeconds"/>: how long <c>show_source</c> shows the source (0 keeps it shown). Texts take <see cref="ObsTemplate"/> placeholders.</summary>
public sealed record ObsRule(string Id, string Event, string Action, string Param = "", string Text = "", double DelaySeconds = 0, double DurationSeconds = 0);

/// <summary>MQTT plugin: broker connection and topics. Events go to <c>&lt;TopicPrefix&gt;/events/&lt;type&gt;</c>, availability to
/// <c>&lt;TopicPrefix&gt;/status</c>. The password is stored as typed.</summary>
public sealed record MqttSettings(bool Enabled = false, string Host = "localhost", int Port = 1883, bool UseTls = false,
    string Username = "", string Password = "", string TopicPrefix = "matchpulse", int Qos = 1);
