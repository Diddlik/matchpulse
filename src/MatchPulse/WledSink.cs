using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MatchPulse;

/// <summary>WLED plugin: lights from WLED controllers react to Game Events through their JSON API (<c>/json/state</c>).</summary>
public sealed record WledSettings(bool Enabled = false, IReadOnlyList<WledDevice>? Devices = null)
{
    public static readonly string[] Actions = ["flash", "preset", "color", "on", "off"];
    public static readonly string[] Effects = ["solid", "blink", "strobe"];

    public IReadOnlyList<WledDevice> Devices { get; init; } = Devices ?? [];

    // Up to 0.4.1 there was one controller ("host" and "rules"); an old config loads it as the first device.
    WledDevice First => Devices.FirstOrDefault() ?? new WledDevice("wled", "WLED", "");

    [JsonPropertyName("host"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyHost { get => null; init { if (value is not null) Devices = [First with { Host = value }, .. Devices.Skip(1)]; } }

    [JsonPropertyName("rules"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WledRule>? LegacyRules { get => null; init { if (value is { Count: > 0 }) Devices = [First with { Rules = value }, .. Devices.Skip(1)]; } }
}

/// <summary>One WLED controller and its own rules.</summary>
public sealed record WledDevice(string Id, string Name, string Host, IReadOnlyList<WledRule>? Rules = null)
{
    public IReadOnlyList<WledRule> Rules { get; init; } = Rules ?? [];
}

/// <summary>When <see cref="Event"/> happens: <c>flash</c> shows <see cref="Color"/> with <see cref="Effect"/> for
/// <see cref="DurationSeconds"/> and then restores the lights; <c>preset</c> loads <see cref="Preset"/>; <c>color</c> sets
/// <see cref="Color"/> for good; <c>on</c> / <c>off</c> switch the lights.</summary>
public sealed record WledRule(string Id, string Event, string Action, string Color = "#FF0000", string Effect = "blink",
    double DurationSeconds = 1.5, int Preset = 1);

/// <summary>
/// Sends the matching WLED rules for each Game Event. A flash saves the current state first and restores it afterwards,
/// in the background, so the next Game Event is not held up; a newer flash takes over the restore of the one before.
/// </summary>
public sealed class WledSink(HttpClient http, WledDevice device, Action<IEventSink, Exception?>? later = null) : IEventSink
{
    static readonly Dictionary<string, int> EffectIds = new() { ["solid"] = 0, ["blink"] = 1, ["strobe"] = 23 };
    JsonNode? _restore; // the state before the running flash
    int _flash;         // which flash may restore

    public WledDevice Device => device;

    string Url => BaseUrl(device.Host);

    /// <summary>"192.168.1.50", "http://192.168.1.50/" or "wled.local" all become "http://host/json".</summary>
    static string BaseUrl(string host)
    {
        var h = host.Trim();
        if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) h = h[7..];
        else if (h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) h = h[8..];
        return $"http://{h.Split('/')[0]}/json";
    }

    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        foreach (var rule in device.Rules.Where(r => r.Event == gameEvent.Type))
            await RunAsync(rule, cancellationToken);
    }

    /// <summary>Asks the controller for its name and version; throws when it cannot be reached.</summary>
    public static async Task<string> TestAsync(HttpClient http, string host, CancellationToken cancellationToken)
    {
        var info = await http.GetFromJsonAsync<JsonObject>($"{BaseUrl(host)}/info", cancellationToken);
        return $"{(string?)info?["name"] ?? "WLED"} {(string?)info?["ver"]}".Trim();
    }

    /// <summary>Runs one rule now (the plugin page's play button).</summary>
    public Task RunAsync(WledRule rule, CancellationToken cancellationToken) => rule.Action switch
    {
        "flash" => FlashAsync(rule, cancellationToken),
        "preset" => PostAsync(new JsonObject { ["on"] = true, ["ps"] = rule.Preset }, cancellationToken),
        "color" => PostAsync(Solid(rule.Color, "solid"), cancellationToken),
        "on" => PostAsync(new JsonObject { ["on"] = true }, cancellationToken),
        "off" => PostAsync(new JsonObject { ["on"] = false }, cancellationToken),
        _ => throw new InvalidDataException($"Unknown WLED action {rule.Action}."),
    };

    async Task FlashAsync(WledRule rule, CancellationToken cancellationToken)
    {
        var flash = Interlocked.Increment(ref _flash);
        // Save the state only when no flash is running, so back-to-back kills restore what was there before the first.
        _restore ??= Restorable(await http.GetFromJsonAsync<JsonObject>($"{Url}/state", cancellationToken));
        await PostAsync(Solid(rule.Color, rule.Effect), cancellationToken);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(rule.DurationSeconds));
            if (flash != Volatile.Read(ref _flash) || _restore is not { } restore) return; // a newer flash restores
            try
            {
                await PostAsync(restore.AsObject(), CancellationToken.None);
                later?.Invoke(this, null);
            }
            catch (Exception e)
            {
                later?.Invoke(this, e);
            }
            finally
            {
                _restore = null;
            }
        });
    }

    // The parts of a state that a flash changes: power, brightness, preset and each segment's colours and effect.
    static JsonNode? Restorable(JsonObject? state) => state is null ? null : new JsonObject
    {
        ["on"] = state["on"]?.DeepClone(),
        ["bri"] = state["bri"]?.DeepClone(),
        ["seg"] = new JsonArray([.. (state["seg"]?.AsArray() ?? []).Select(s => (JsonNode)new JsonObject
        {
            ["id"] = s?["id"]?.DeepClone(),
            ["col"] = s?["col"]?.DeepClone(),
            ["fx"] = s?["fx"]?.DeepClone(),
            ["sx"] = s?["sx"]?.DeepClone(),
            ["pal"] = s?["pal"]?.DeepClone(),
        })]),
    };

    static JsonObject Solid(string color, string effect)
    {
        var c = Convert.FromHexString(color.TrimStart('#'));
        return new JsonObject
        {
            ["on"] = true,
            ["bri"] = 255,
            ["seg"] = new JsonArray(new JsonObject
            {
                ["col"] = new JsonArray(new JsonArray(c[0], c[1], c[2])),
                ["fx"] = EffectIds.GetValueOrDefault(effect),
                ["sx"] = 200,
            }),
        };
    }

    async Task PostAsync(JsonObject state, CancellationToken cancellationToken)
    {
        // With a fixed length: WLED's web server (ESP8266/ESP32) answers 400 to a chunked request body.
        using var body = new StringContent(state.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{Url}/state", body, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WLED answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
    }

    public override string ToString() => $"WLED {device.Name} ({device.Host})";
}
