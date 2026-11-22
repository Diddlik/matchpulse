using System.Text.Json;

namespace MatchPulse;

/// <summary>Local config file, for example <c>{ "webhooks": ["http://localhost:8080/bf6"], "checksPerSecond": 10 }</c>.</summary>
public sealed record Config(IReadOnlyList<Uri> Webhooks, double ChecksPerSecond = 10)
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
