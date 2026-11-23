using System.Text.Json;

namespace MatchPulse;

/// <summary>A Game Event. Property names are the public JSON contract for consumers.</summary>
public sealed record GameEvent(
    string Type,
    string? MatchId, // null for a kill outside a Match (multiplayer)
    int Sequence,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string> Data)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}
