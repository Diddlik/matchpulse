using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Takes timestamped frames and emits Game Events. No I/O; time comes from the frames only.
/// A Match starts when the in-match HUD is confirmed and ends on an end-of-match screen, or when the
/// in-match HUD has been absent for <c>hudLostAfter</c>. Kills are reported only inside a Match.
/// </summary>
public sealed class MatchDetector
{
    readonly ILogger _log;
    readonly string _gameLanguage;
    readonly bool _useKillCounterFlash;
    Size _frameSize;
    Mat _killBadge = null!, _killMarker = null!, _inMatchHud = null!, _endVictory = null!, _endDefeat = null!;
    Mat? _killCounter;
    readonly TimeSpan _hudLostAfter;

    // HSV bands of the colour mask Cues.
    static readonly (Scalar, Scalar)[] KillMarkerRed = [(new(0, 121, 171), new(8, 255, 255)), (new(172, 121, 171), new(180, 255, 255))];
    static readonly (Scalar, Scalar)[] VictoryGold = [(new(20, 91, 151), new(36, 255, 255))];
    static readonly (Scalar, Scalar)[] DefeatRed = [(new(0, 91, 151), new(18, 255, 255)), (new(170, 91, 151), new(180, 255, 255))];

    // Match state: no Match while _matchId is null.
    string? _matchId;
    int _sequence;
    DateTimeOffset _hudSeenAt;
    readonly Hysteresis _hud = new(Cue.InMatchHud.K);
    readonly Hysteresis _victory = new(Cue.EndVictory.K);
    readonly Hysteresis _defeat = new(Cue.EndDefeat.K);

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
    readonly List<DateTimeOffset> _killMarkers = []; // not yet used for a kill; the newest one in the window is used first

    // REDSEC fallback for a missed Kill Badge: a kill counter flash after a Kill Marker is held
    // until the Kill Marker window closes and becomes a kill only if no Kill Badge was seen.
    readonly Hysteresis _flash = new(Cue.KillCounterFlash.K);
    (DateTimeOffset At, DateTimeOffset KillMarker)? _pendingFlash;

    /// <param name="killCounterFlash">Use the REDSEC kill counter flash as a fallback; off where there is no counter bar.</param>
    /// <param name="hudLostAfter">How long the in-match HUD may be absent before the Match ends (default 120 s, longer than a Jammer).</param>
    /// <param name="gameLanguage">Selects the templates of text Cues (<see cref="Config.GameLanguage"/>).</param>
    public MatchDetector(ILogger? log = null, bool killCounterFlash = true, TimeSpan? hudLostAfter = null, string gameLanguage = "de")
    {
        _log = log ?? NullLogger.Instance;
        _hudLostAfter = hudLostAfter ?? TimeSpan.FromSeconds(120);
        _useKillCounterFlash = killCounterFlash;
        _gameLanguage = gameLanguage;
    }

    public IReadOnlyList<GameEvent> Process(Frame frame)
    {
        if (frame.Image.Size() != _frameSize) LoadTemplates(frame.Image.Size());
        var events = new List<GameEvent>();
        var hud = GrayScore(frame.Image, Cue.InMatchHud, _inMatchHud) >= Cue.InMatchHud.Threshold;

        if (_matchId is null)
        {
            if (_hud.Update(hud, frame.Timestamp) is { } startedAt)
            {
                _log.LogInformation("Cue {Cue} seen at {At:O}", Cue.InMatchHud.Name, startedAt);
                StartMatch();
                _hudSeenAt = frame.Timestamp;
                events.Add(Emit("match_started", startedAt));
            }
            return events;
        }

        if (hud) _hudSeenAt = frame.Timestamp;
        var victory = _victory.Update(MaskScore(frame.Image, Cue.EndVictory, _endVictory, VictoryGold) >= Cue.EndVictory.Threshold, frame.Timestamp);
        var defeat = _defeat.Update(MaskScore(frame.Image, Cue.EndDefeat, _endDefeat, DefeatRed) >= Cue.EndDefeat.Threshold, frame.Timestamp);
        if ((victory ?? defeat) is { } endedAt)
        {
            var end = victory is null ? Cue.EndDefeat : Cue.EndVictory;
            _log.LogInformation("Cue {Cue} seen at {At:O}", end.Name, endedAt);
            return EndMatch(endedAt, victory is null ? "defeat" : "victory", "end_screen");
        }
        if (frame.Timestamp - _hudSeenAt > _hudLostAfter)
        {
            _log.LogInformation("Cue {Cue} absent since {At:O}", Cue.InMatchHud.Name, _hudSeenAt);
            return EndMatch(frame.Timestamp, "unknown", "hud_lost");
        }

        var killMarker = MaskScore(frame.Image, Cue.KillMarker, _killMarker, KillMarkerRed) >= Cue.KillMarker.Threshold;
        if (killMarker && !_killMarkerVisible)
        {
            _log.LogInformation("Cue {Cue} seen at {At:O}", Cue.KillMarker.Name, frame.Timestamp);
            _killMarkers.Add(frame.Timestamp);
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
                var at = _rise[0].At;
                _log.LogInformation("Cue {Cue} seen: {Count} in Score Popup at {At:O}", Cue.KillBadge.Name, confirmed, at);
                var marker = _killMarkers.FindLastIndex(m => at - m <= KillMarkerWindow);
                if (marker >= 0)
                {
                    events.Add(Emit("kill", at));
                    _killMarkers.RemoveAt(marker);
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

        if (_flash.Update(KillCounterFlashes(frame.Image), frame.Timestamp) is { } flashAt)
        {
            _log.LogInformation("Cue {Cue} seen at {At:O}", Cue.KillCounterFlash.Name, flashAt);
            var marker = _killMarkers.FindLastIndex(m => flashAt - m <= KillMarkerWindow);
            if (marker >= 0) _pendingFlash = (flashAt, _killMarkers[marker]);
        }

        if (_pendingFlash is { } flash && frame.Timestamp > flash.KillMarker + KillMarkerWindow)
            events.Add(EmitPendingFlash(flash));

        return events;
    }

    /// <summary>The game closed at <paramref name="at"/>: ends a running Match with <c>result: unknown</c>, <c>reason: game_exited</c>.</summary>
    public IReadOnlyList<GameEvent> GameExited(DateTimeOffset at) =>
        _matchId is null ? [] : EndMatch(at, "unknown", "game_exited");

    void StartMatch()
    {
        _matchId = Guid.NewGuid().ToString("N");
        _sequence = 0;
        _victory.Reset();
        _defeat.Reset();
        _killBadges = 0;
        _rise.Clear();
        _fall.Clear();
        _killMarkerVisible = false;
        _killMarkers.Clear();
        _flash.Reset();
        _pendingFlash = null;
    }

    // A held kill counter flash belongs to the Match that ends, so it is sent before match_ended.
    List<GameEvent> EndMatch(DateTimeOffset at, string result, string reason)
    {
        List<GameEvent> events = _pendingFlash is { } flash ? [EmitPendingFlash(flash)] : [];
        events.Add(Emit("match_ended", at, new() { ["result"] = result, ["reason"] = reason }));
        _matchId = null;
        _hud.Reset();
        return events;
    }

    GameEvent EmitPendingFlash((DateTimeOffset At, DateTimeOffset KillMarker) flash)
    {
        _pendingFlash = null;
        _killMarkers.Remove(flash.KillMarker);
        return Emit("kill", flash.At);
    }

    // Templates are scaled to the frame size, so they are (re)loaded when it changes.
    void LoadTemplates(Size frame)
    {
        _frameSize = frame;
        _killBadge = Cue.KillBadge.LoadTemplate(frame, _gameLanguage);
        _killMarker = Cue.KillMarker.LoadTemplate(frame, _gameLanguage);
        if (_useKillCounterFlash) _killCounter = Cue.KillCounterFlash.LoadTemplate(frame, _gameLanguage);
        _inMatchHud = Cue.InMatchHud.LoadTemplate(frame, _gameLanguage);
        _endVictory = Cue.EndVictory.LoadTemplate(frame, _gameLanguage);
        _endDefeat = Cue.EndDefeat.LoadTemplate(frame, _gameLanguage);
    }

    GameEvent Emit(string type, DateTimeOffset at, Dictionary<string, string>? data = null) =>
        new(type, _matchId!, ++_sequence, at, data ?? []);

    static double GrayScore(Mat image, Cue cue, Mat template)
    {
        using var region = cue.Crop(image);
        using var gray = region.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var scores = gray.MatchTemplate(template, TemplateMatchModes.CCoeffNormed);
        scores.MinMaxLoc(out _, out double max);
        return max;
    }

    // Matches a binary mask template against the frame pixels that fall in one of the HSV bands.
    static double MaskScore(Mat image, Cue cue, Mat template, (Scalar Low, Scalar High)[] bands)
    {
        using var region = cue.Crop(image);
        using var hsv = region.CvtColor(ColorConversionCodes.BGR2HSV);
        using var mask = new Mat(hsv.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var (low, high) in bands)
        {
            using var band = hsv.InRange(low, high);
            Cv2.BitwiseOr(mask, band, mask);
        }
        using var scores = mask.MatchTemplate(template, TemplateMatchModes.CCorrNormed);
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

    /// <summary>A Cue counts as seen after K consecutive matching checks; returns the time of the first of them, once.</summary>
    sealed class Hysteresis(int k)
    {
        int _checks;
        DateTimeOffset _from;

        public DateTimeOffset? Update(bool seen, DateTimeOffset at)
        {
            _checks = seen ? _checks + 1 : 0;
            if (_checks == 1) _from = at;
            return _checks == k ? _from : null;
        }

        public void Reset() => _checks = 0;
    }
}
