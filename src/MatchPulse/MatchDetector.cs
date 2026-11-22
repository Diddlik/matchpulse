using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Takes timestamped frames and emits Game Events. No I/O; time comes from the frames only.
/// For now a Match is treated as already running from the first frame (Match boundaries are ticket 04).
/// </summary>
public sealed class MatchDetector
{
    readonly ILogger _log;
    readonly Mat _killBadge;
    readonly Mat _killMarker;
    readonly Mat? _killCounter;
    readonly string _matchId = Guid.NewGuid().ToString("N");
    int _sequence;

    // Kill Badges currently confirmed in the Score Popup, and the runs of checks showing more or fewer than that.
    // Fewer must persist longer, so a badge missed on a bright background for a check or two is not counted again.
    const int KillBadgeReleaseChecks = 5;
    int _killBadges;
    readonly List<(DateTimeOffset At, int Count)> _rise = [];
    readonly List<int> _fall = [];

    // A Kill Badge is a Kill only shortly after a Kill Marker, and each Kill Marker confirms at most one kill:
    // the Squad finishing the Player's knockdown, or a squad wipe killing it, adds a Kill Badge without one.
    // Measured: 0.4-0.6 s normally, 1.2 s when the Score Popup queues the Kill Badge behind a Knockdown Badge.
    static readonly TimeSpan KillMarkerWindow = TimeSpan.FromSeconds(1.5);
    bool _killMarkerVisible;
    DateTimeOffset _killMarkerAt = DateTimeOffset.MinValue; // MinValue once used for a kill

    // REDSEC fallback for a missed Kill Badge: a kill counter flash after a Kill Marker is held
    // until the Kill Marker window closes and becomes a kill only if no Kill Badge was seen.
    int _flashChecks;
    DateTimeOffset _flashFrom;
    (DateTimeOffset At, DateTimeOffset Until)? _pendingFlash;

    /// <param name="killCounterFlash">Use the REDSEC kill counter flash as a fallback; off where there is no counter bar.</param>
    public MatchDetector(ILogger? log = null, bool killCounterFlash = true)
    {
        _log = log ?? NullLogger.Instance;
        _killBadge = Cue.KillBadge.LoadTemplate();
        _killMarker = Cue.KillMarker.LoadTemplate();
        if (killCounterFlash) _killCounter = Cue.KillCounterFlash.LoadTemplate();
    }

    public IReadOnlyList<GameEvent> Process(Frame frame)
    {
        var events = new List<GameEvent>();

        var killMarker = KillMarkerScore(frame.Image) >= Cue.KillMarker.Threshold;
        if (killMarker && !_killMarkerVisible)
        {
            _log.LogInformation("Cue {Cue} seen at {At:O}", Cue.KillMarker.Name, frame.Timestamp);
            _killMarkerAt = frame.Timestamp;
        }
        _killMarkerVisible = killMarker;

        var count = CountKillBadges(frame.Image);

        if (count > _killBadges)
        {
            _fall.Clear();
            _rise.Add((frame.Timestamp, count));
            if (_rise.Count >= Cue.KillBadge.K)
            {
                var confirmed = _rise.Min(r => r.Count);
                _log.LogInformation("Cue {Cue} seen: {Count} in Score Popup at {At:O}", Cue.KillBadge.Name, confirmed, _rise[0].At);
                if (_rise[0].At - _killMarkerAt <= KillMarkerWindow)
                {
                    events.Add(Emit("kill", _rise[0].At));
                    _killMarkerAt = DateTimeOffset.MinValue;
                    _pendingFlash = null;
                }
                _killBadges = confirmed;
                _rise.Clear();
            }
        }
        else
        {
            _rise.Clear();
            if (count == _killBadges)
                _fall.Clear();
            else
            {
                _fall.Add(count);
                if (_fall.Count >= KillBadgeReleaseChecks)
                {
                    _killBadges = _fall.Max();
                    _fall.Clear();
                }
            }
        }

        _flashChecks = KillCounterFlashes(frame.Image) ? _flashChecks + 1 : 0;
        if (_flashChecks == 1) _flashFrom = frame.Timestamp;
        if (_flashChecks == Cue.KillCounterFlash.K)
        {
            _log.LogInformation("Cue {Cue} seen at {At:O}", Cue.KillCounterFlash.Name, _flashFrom);
            if (_flashFrom - _killMarkerAt <= KillMarkerWindow)
                _pendingFlash = (_flashFrom, _killMarkerAt + KillMarkerWindow);
        }

        if (_pendingFlash is { } flash && frame.Timestamp > flash.Until)
        {
            events.Add(Emit("kill", flash.At));
            _pendingFlash = null;
        }

        return events;
    }

    GameEvent Emit(string type, DateTimeOffset at) =>
        new(type, _matchId, ++_sequence, at, new Dictionary<string, string>());

    double KillMarkerScore(Mat image)
    {
        using var region = Cue.KillMarker.Crop(image);
        using var hsv = region.CvtColor(ColorConversionCodes.BGR2HSV);
        using var low = hsv.InRange(new Scalar(0, 121, 171), new Scalar(8, 255, 255));
        using var high = hsv.InRange(new Scalar(172, 121, 171), new Scalar(180, 255, 255));
        using var red = new Mat();
        Cv2.BitwiseOr(low, high, red);
        using var scores = red.MatchTemplate(_killMarker, TemplateMatchModes.CCorrNormed);
        scores.MinMaxLoc(out _, out double max);
        return max;
    }

    bool KillCounterFlashes(Mat image)
    {
        if (_killCounter is null) return false;
        using var region = Cue.KillCounterFlash.Crop(image);
        using var gray = region.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var scores = gray.MatchTemplate(_killCounter, TemplateMatchModes.CCoeffNormed);
        scores.MinMaxLoc(out _, out double max, out _, out Point at);
        if (max < Cue.KillCounterFlash.Threshold) return false;

        // Most bright pixels of the located skull must be green.
        using var hsv = new Mat(region, new Rect(at, _killCounter.Size())).CvtColor(ColorConversionCodes.BGR2HSV);
        using var bright = hsv.InRange(new Scalar(0, 0, 151), new Scalar(180, 255, 255));
        using var green = hsv.InRange(new Scalar(35, 51, 151), new Scalar(85, 255, 255));
        return green.CountNonZero() > 0.5 * bright.CountNonZero();
    }

    int CountKillBadges(Mat image)
    {
        using var region = Cue.KillBadge.Crop(image);
        using var gray = region.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var scores = gray.MatchTemplate(_killBadge, TemplateMatchModes.CCoeffNormed);

        // Count every match above the threshold, blanking each one's neighbourhood so it is counted once.
        var count = 0;
        while (true)
        {
            scores.MinMaxLoc(out _, out double max, out _, out Point at);
            if (max < Cue.KillBadge.Threshold) return count;
            count++;
            scores.Rectangle(new Rect(at.X - _killBadge.Width, at.Y - _killBadge.Height, 2 * _killBadge.Width, 2 * _killBadge.Height), Scalar.All(-1), -1);
        }
    }
}
