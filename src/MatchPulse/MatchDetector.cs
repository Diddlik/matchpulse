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
    readonly string _matchId = Guid.NewGuid().ToString("N");
    int _sequence;

    // Kill Badges currently confirmed in the Score Popup, and the runs of checks showing more or fewer than that.
    // Fewer must persist longer, so a badge missed on a bright background for a check or two is not counted again.
    const int KillBadgeReleaseChecks = 5;
    int _killBadges;
    readonly List<(DateTimeOffset At, int Count)> _rise = [];
    readonly List<int> _fall = [];

    public MatchDetector(ILogger? log = null)
    {
        _log = log ?? NullLogger.Instance;
        _killBadge = Cue.KillBadge.LoadTemplate();
    }

    public IReadOnlyList<GameEvent> Process(Frame frame)
    {
        var events = new List<GameEvent>();
        var count = CountKillBadges(frame.Image);

        if (count > _killBadges)
        {
            _fall.Clear();
            _rise.Add((frame.Timestamp, count));
            if (_rise.Count >= Cue.KillBadge.K)
            {
                var confirmed = _rise.Min(r => r.Count);
                _log.LogInformation("Cue {Cue} seen: {Count} in Score Popup at {At:O}", Cue.KillBadge.Name, confirmed, _rise[0].At);
                for (var i = _killBadges; i < confirmed; i++)
                    events.Add(Emit("kill", _rise[0].At));
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

        return events;
    }

    GameEvent Emit(string type, DateTimeOffset at) =>
        new(type, _matchId, ++_sequence, at, new Dictionary<string, string>());

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
