using OpenCvSharp;

namespace MatchPulse;

/// <summary>One check: a captured BGR frame and the time it was shown. Live frames hold only the Cue regions
/// unless <see cref="Full"/> (the whole screen, captured on request for a kill picture).</summary>
public sealed record Frame(DateTimeOffset Timestamp, Mat Image)
{
    public bool Full { get; init; }
}
