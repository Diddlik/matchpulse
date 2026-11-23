using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Takes timestamped frames and emits Game Events. No I/O; time comes from the frames only.
/// A Match starts when the in-match HUD is confirmed and ends on an end-of-match screen, or when the
/// in-match HUD has been absent for <c>hudLostAfter</c>. Kills are reported everywhere; outside a Match
/// (multiplayer) they carry no matchId and are found by the plain Kill Badge.
/// </summary>
public sealed class MatchDetector
{
    readonly ILogger _log;
    readonly string _gameLanguage;
    readonly bool _useKillCounterFlash;
    Size _frameSize;
    Mat _killBadge = null!, _killBadgePlain = null!, _inMatchHud = null!, _endVictory = null!, _endDefeat = null!;
    Mat? _killCounter;
    Mat[] _killMarker = [];
    readonly TimeSpan _hudLostAfter;

    // HSV bands of the colour mask Cues.
    readonly (Scalar, Scalar)[]? _killMarkerBands; // null: Kill Marker colour not distinct, a Kill Badge alone is a kill
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
    // The Kill Marker shrinks to about a third while aiming down sights.
    static readonly double[] KillMarkerScales = [1.0, 0.7, 0.5, 0.35];
    bool _killMarkerVisible;
    readonly List<DateTimeOffset> _killMarkers = []; // not yet used for a kill; the newest one in the window is used first

    // REDSEC fallback for a missed Kill Badge: a kill counter flash after a Kill Marker is held
    // until the Kill Marker window closes and becomes a kill only if no Kill Badge was seen.
    readonly Hysteresis _flash = new(Cue.KillCounterFlash.K);
    (DateTimeOffset At, DateTimeOffset KillMarker)? _pendingFlash;
    DateTimeOffset? _badgeKillAt; // a kill counter flash this close to a Kill Badge kill belongs to it

    /// <summary>Diagnostic mode: a case where a Kill may have been missed, with the time of its Cue.
    /// <c>kill_badge_without_kill_marker</c> when a Kill Badge is not confirmed, <c>kill_marker_without_kill_badge</c> when a Kill Marker's window closes unused.</summary>
    public event Action<string, DateTimeOffset>? Unconfirmed;

    /// <param name="killCounterFlash">Use the REDSEC kill counter flash as a fallback; off where there is no counter bar.</param>
    /// <param name="hudLostAfter">How long the in-match HUD may be absent before the Match ends (default 120 s, longer than a Jammer).</param>
    /// <param name="gameLanguage">Selects the templates of text Cues (<see cref="Config.GameLanguage"/>).</param>
    /// <param name="killMarkerColor">The Kill Marker colour set in the game (<see cref="Config.KillMarkerColor"/>).</param>
    public MatchDetector(ILogger? log = null, bool killCounterFlash = true, TimeSpan? hudLostAfter = null, string gameLanguage = "de", string killMarkerColor = "#FF0000")
    {
        _log = log ?? NullLogger.Instance;
        _killMarkerBands = KillMarkerBands(killMarkerColor);
        if (_killMarkerBands is null)
            _log.LogWarning("Kill Marker colour {Color} cannot be told from the white hit marker: every Kill Badge counts as a kill, Squad-finished knockdowns included", killMarkerColor);
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
                return events;
            }
        }
        else if (EndOfMatch(frame, hud) is { } ended)
            return ended;

        Kills(frame, events);
        return events;
    }

    List<GameEvent>? EndOfMatch(Frame frame, bool hud)
    {
        if (hud) _hudSeenAt = frame.Timestamp;
        var victory = _victory.Update(MaskScore(frame.Image, Cue.EndVictory, VictoryGold, _endVictory) >= Cue.EndVictory.Threshold, frame.Timestamp);
        var defeat = _defeat.Update(MaskScore(frame.Image, Cue.EndDefeat, DefeatRed, _endDefeat) >= Cue.EndDefeat.Threshold, frame.Timestamp);
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
        return null;
    }

    void Kills(Frame frame, List<GameEvent> events)
    {
        var killMarker = _killMarkerBands is not null && MaskScore(frame.Image, Cue.KillMarker, _killMarkerBands, _killMarker) >= Cue.KillMarker.Threshold;
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
                _log.LogInformation("Cue {Cue} seen: {Count} in Score Popup at {At:O}", (_matchId is null ? Cue.KillBadgePlain : Cue.KillBadge).Name, confirmed, at);
                var marker = _killMarkers.FindLastIndex(m => at - m <= KillMarkerWindow);
                if (marker >= 0 || _killMarkerBands is null)
                {
                    events.Add(Emit("kill", at));
                    if (marker >= 0) _killMarkers.RemoveAt(marker);
                    _pendingFlash = null;
                    _badgeKillAt = at;
                }
                else Unconfirmed?.Invoke("kill_badge_without_kill_marker", at);
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
            if (_badgeKillAt is { } badgeKill && (flashAt - badgeKill).Duration() <= KillMarkerWindow) _badgeKillAt = null;
            else if (marker >= 0) _pendingFlash = (flashAt, _killMarkers[marker]);
        }

        if (_pendingFlash is { } flash && frame.Timestamp > flash.KillMarker + KillMarkerWindow)
            events.Add(EmitPendingFlash(flash));

        foreach (var expired in _killMarkers.Where(m => frame.Timestamp - m > KillMarkerWindow && _pendingFlash?.KillMarker != m).ToList())
        {
            _killMarkers.Remove(expired);
            Unconfirmed?.Invoke("kill_marker_without_kill_badge", expired);
        }
    }

    /// <summary>The game closed at <paramref name="at"/>: ends a running Match with <c>result: unknown</c>, <c>reason: game_exited</c>.</summary>
    public IReadOnlyList<GameEvent> GameExited(DateTimeOffset at) =>
        _matchId is null ? [] : EndMatch(at, "unknown", "game_exited");

    /// <summary>MatchPulse stopped at <paramref name="at"/>: ends a running Match with <c>result: unknown</c>, <c>reason: stopped</c>.</summary>
    public IReadOnlyList<GameEvent> Stopped(DateTimeOffset at) =>
        _matchId is null ? [] : EndMatch(at, "unknown", "stopped");

    void StartMatch()
    {
        _matchId = Guid.NewGuid().ToString("N");
        _victory.Reset();
        _defeat.Reset();
        ResetKills();
    }

    // Per Match, and back to multiplayer kills (other Kill Badge template) after it.
    void ResetKills()
    {
        _sequence = 0;
        _killBadges = 0;
        _rise.Clear();
        _fall.Clear();
        _killMarkerVisible = false;
        _killMarkers.Clear();
        _flash.Reset();
        _pendingFlash = null;
        _badgeKillAt = null;
    }

    // A held kill counter flash belongs to the Match that ends, so it is sent before match_ended.
    List<GameEvent> EndMatch(DateTimeOffset at, string result, string reason)
    {
        List<GameEvent> events = _pendingFlash is { } flash ? [EmitPendingFlash(flash)] : [];
        events.Add(Emit("match_ended", at, new() { ["result"] = result, ["reason"] = reason }));
        _matchId = null;
        _hud.Reset();
        ResetKills();
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
        _killBadgePlain = Cue.KillBadgePlain.LoadTemplate(frame, _gameLanguage);
        using var killMarker = Cue.KillMarker.LoadTemplate(frame, _gameLanguage);
        foreach (var scaled in _killMarker) scaled.Dispose();
        _killMarker = KillMarkerScales.Select(s => killMarker.Resize(new Size(Math.Round(killMarker.Width * s), Math.Round(killMarker.Height * s)), 0, 0, InterpolationFlags.Area)).ToArray();
        if (_useKillCounterFlash) _killCounter = Cue.KillCounterFlash.LoadTemplate(frame, _gameLanguage);
        _inMatchHud = Cue.InMatchHud.LoadTemplate(frame, _gameLanguage);
        _endVictory = Cue.EndVictory.LoadTemplate(frame, _gameLanguage);
        _endDefeat = Cue.EndDefeat.LoadTemplate(frame, _gameLanguage);
    }

    GameEvent Emit(string type, DateTimeOffset at, Dictionary<string, string>? data = null) =>
        new(type, _matchId, ++_sequence, at, data ?? []);

    static double GrayScore(Mat image, Cue cue, Mat template)
    {
        using var region = cue.Crop(image);
        using var gray = region.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var scores = gray.MatchTemplate(template, TemplateMatchModes.CCoeffNormed);
        scores.MinMaxLoc(out _, out double max);
        return max;
    }

    /// <summary>
    /// HSV bands of the Kill Marker: hue within 8 of <paramref name="color"/> (#RRGGBB), saturation and value above 120.
    /// Null when the colour itself is not that saturated and bright (white, grey, dark): then it looks like the white hit marker.
    /// </summary>
    public static (Scalar, Scalar)[]? KillMarkerBands(string color)
    {
        var rgb = Convert.FromHexString(color[1..]);
        using var pixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar(rgb[2], rgb[1], rgb[0]));
        using var hsv = pixel.CvtColor(ColorConversionCodes.BGR2HSV);
        var (hue, saturation, value) = (hsv.At<Vec3b>(0, 0)[0], hsv.At<Vec3b>(0, 0)[1], hsv.At<Vec3b>(0, 0)[2]);
        if (saturation <= 120 || value <= 120) return null;
        int low = hue - 8, high = hue + 8;
        static Scalar From(int h) => new(h, 121, 121);
        static Scalar To(int h) => new(h, 255, 255);
        if (low < 0) return [(From(0), To(high)), (From(180 + low), To(180))];
        if (high > 179) return [(From(low), To(180)), (From(0), To(high - 180))];
        return [(From(low), To(high))];
    }

    // Matches binary mask templates against the frame pixels that fall in one of the HSV bands; the best template counts.
    static double MaskScore(Mat image, Cue cue, (Scalar Low, Scalar High)[] bands, params Mat[] templates)
    {
        using var region = cue.Crop(image);
        using var hsv = region.CvtColor(ColorConversionCodes.BGR2HSV);
        using var mask = new Mat(hsv.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var (low, high) in bands)
        {
            using var band = hsv.InRange(low, high);
            Cv2.BitwiseOr(mask, band, mask);
        }
        return templates.Max(template =>
        {
            using var scores = mask.MatchTemplate(template, TemplateMatchModes.CCorrNormed);
            scores.MinMaxLoc(out _, out double max);
            return max;
        });
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
        var badge = _matchId is null ? _killBadgePlain : _killBadge;
        using var scores = gray.MatchTemplate(badge, TemplateMatchModes.CCoeffNormed);

        // Count every match above the threshold, blanking each one's neighbourhood so it is counted once.
        var count = 0;
        while (true)
        {
            scores.MinMaxLoc(out _, out double max, out _, out Point at);
            if (max < Cue.KillBadge.Threshold) return count;
            count++;
            scores.Rectangle(new Rect(at.X - badge.Width, at.Y - badge.Height, 2 * badge.Width, 2 * badge.Height), Scalar.All(-1), -1);
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
