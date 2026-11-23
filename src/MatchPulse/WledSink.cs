using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace MatchPulse;

/// <summary>WLED plugin: lights from a WLED controller react to Game Events through its JSON API (<c>/json/state</c>).</summary>
public sealed record WledSettings(bool Enabled = false, string Host = "", IReadOnlyList<WledRule>? Rules = null)
{
    public static readonly string[] Actions = ["flash", "preset", "color", "on", "off"];
    public static readonly string[] Effects = ["solid", "blink", "strobe"];

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
public sealed class WledSink(HttpClient http, WledSettings settings, Action<IEventSink, Exception?>? later = null) : IEventSink
{
    static readonly Dictionary<string, int> EffectIds = new() { ["solid"] = 0, ["blink"] = 1, ["strobe"] = 23 };
    JsonNode? _restore; // the state before the running flash
    int _flash;         // which flash may restore

    public WledSettings Settings => settings;

    string Url => $"http://{settings.Host.Trim().TrimEnd('/')}/json";

    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        foreach (var rule in settings.Rules.Where(r => r.Event == gameEvent.Type))
            await RunAsync(rule, cancellationToken);
    }

    /// <summary>Asks the controller for its name and version; throws when it cannot be reached.</summary>
    public static async Task<string> TestAsync(HttpClient http, WledSettings settings, CancellationToken cancellationToken)
    {
        var info = await http.GetFromJsonAsync<JsonObject>($"http://{settings.Host.Trim().TrimEnd('/')}/json/info", cancellationToken);
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
        using var response = await http.PostAsJsonAsync($"{Url}/state", state, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public override string ToString() => $"WLED {settings.Host}";
}
