using OpenCvSharp;

namespace MatchPulse;

/// <summary>One check: a captured BGR frame and the time it was shown.</summary>
public sealed record Frame(DateTimeOffset Timestamp, Mat Image);
