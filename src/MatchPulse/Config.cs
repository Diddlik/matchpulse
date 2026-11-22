using System.Text.Json;

namespace MatchPulse;

/// <summary>Local config file, for example <c>{ "webhooks": ["http://localhost:8080/bf6"], "scripts": ["pwsh -File C:\obs.ps1"], "checksPerSecond": 10 }</c>.
/// Each script is a command line run through <c>cmd.exe /c</c> once per Game Event.</summary>
public sealed record Config(IReadOnlyList<Uri> Webhooks, double ChecksPerSecond = 10, IReadOnlyList<string>? Scripts = null)
{
    public static readonly string DefaultPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatchPulse", "config.json");

    public static Config Load(string path)
    {
        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"{path} is empty.");
        if (config.Webhooks is null) throw new InvalidDataException($"{path}: \"webhooks\" is missing.");
        foreach (var url in config.Webhooks)
            if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
                throw new InvalidDataException($"{path}: webhook {url} is not an absolute http(s) URL.");
        if (config.ChecksPerSecond is < 5 or > 10)
            throw new InvalidDataException($"{path}: \"checksPerSecond\" must be between 5 and 10.");
        return config;
    }
}
