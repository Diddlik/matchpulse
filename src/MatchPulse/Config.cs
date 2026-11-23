using System.Text.Json;
using System.Text.RegularExpressions;

namespace MatchPulse;

/// <summary>
/// Local config file, for example
/// <c>{ "webhooks": ["http://localhost:8080/bf6"], "scripts": ["pwsh -File C:\obs.ps1"], "gameLanguage": "de", "uiLanguage": "en", "checksPerSecond": 10, "hudLostAfterSeconds": 120, "killMarkerColor": "#FF0000" }</c>.
/// <c>killMarkerColor</c> is the Kill Marker colour set in the game settings, as <c>#RRGGBB</c>.
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
    bool NotifyOnProblems = true)
{
    public static readonly string DefaultPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatchPulse", "config.json");

    public static readonly string[] Languages = ["de", "en"];
    public static readonly string[] Themes = ["system", "light", "dark"];

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<string> Scripts { get; init; } = Scripts ?? [];

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
        if (KillMarkerColor is null || !Regex.IsMatch(KillMarkerColor, "^#[0-9A-Fa-f]{6}$"))
            throw new InvalidDataException($"{path}: \"killMarkerColor\" must be a colour like #FF0000.");
    }
}
