using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Takes timestamped frames and emits Game Events. No I/O; time comes from the frames only.
/// A Match starts when the in-match HUD is confirmed and ends on an end-of-match screen, or when the
/// in-match HUD has been absent for <c>hudLostAfter</c>. A REDSEC Match has the counter bar top right and ends on the
/// REDSEC end screen; a multiplayer Match (a round) has the score bar top centre and ends on the round's SIEG or
/// NIEDERLAGE banner. Inside a REDSEC Match it also reports the Player's knockdowns and death (spectating).
/// Kills are reported everywhere; in multiplayer (inside a round or outside any Match, then without matchId) they
/// are found by the plain Kill Badge, which there also counts without a Kill Marker when it opens an empty Score
/// Popup, and again when it is redrawn after a Kill Marker (multi-kill).
/// </summary>
public sealed class MatchDetector
{
    readonly ILogger _log;
    readonly string _gameLanguage;
    readonly bool _useKillCounterFlash;
    Size _frameSize;
    Mat _killBadge = null!, _killBadgePlain = null!, _knockdownBadge = null!, _inMatchHud = null!, _endVictory = null!, _endDefeat = null!;
    Mat? _killCounter, _spectate;
    Mat[] _killMarker = [];
    readonly TimeSpan _hudLostAfter;

    // HSV bands of the colour mask Cues.
    readonly (Scalar, Scalar)[]? _killMarkerBands; // null: Kill Marker colour not distinct, a Kill Badge alone is a kill
    static readonly (Scalar, Scalar)[] VictoryGold = [(new(20, 91, 151), new(36, 255, 255))];
    static readonly (Scalar, Scalar)[] DefeatRed = [(new(0, 91, 151), new(18, 255, 255)), (new(170, 91, 151), new(180, 255, 255))];
    static readonly (Scalar, Scalar)[] TeamBlue = [(new(85, 80, 150), new(105, 255, 255))];
    static readonly (Scalar, Scalar)[] TeamRed = [(new(0, 80, 150), new(15, 255, 255)), (new(170, 80, 150), new(180, 255, 255))];
    Mat[] _mpEndEmblems = [];
    Mat? _mpScoreBar;

    // Match state: no Match while _matchId is null.
    string? _matchId;
    bool _multiplayerMatch;
    bool Redsec => _matchId is not null && !_multiplayerMatch;
    int _sequence;
    DateTimeOffset _hudSeenAt;
    readonly Hysteresis _hud = new(Cue.InMatchHud.K);
    readonly Hysteresis _mpHud = new(Cue.MpScoreBlue.K);
    readonly Hysteresis _victory = new(Cue.EndVictory.K);
    readonly Hysteresis _defeat = new(Cue.EndDefeat.K);

    // Kill Badges currently confirmed in the Score Popup, and the runs of checks showing more or fewer than that.
    // Fewer must persist longer, so a badge missed on a bright background for a check or two is not counted again.
    const int KillBadgeReleaseChecks = 5;
    readonly BadgeTally _killBadges = new(Cue.KillBadge.K), _knockdownBadges = new(Cue.KnockdownBadge.K);
    int _kills; // the Player's kills in this Match (outside a Match: since the last one), sent as data.kills

    // A Knockdown Badge after a Kill Marker is held until that marker's window closes: when a Kill Badge takes the
    // same marker, the Score Popup only queued it behind the Kill Badge's credit (seen under a Jammer), so no knockdown.
    (DateTimeOffset At, DateTimeOffset KillMarker)? _pendingKnockdown;
    DateTimeOffset? _killBadgeAt; // the last Kill Badge, counted or not
    bool _knockdownBadgesSeenEmpty;

    // REDSEC: the Player's knockdown is the Player's kill, sent at that moment with the knockdown. The later Kill Badge
    // of that opponent (the Player finishing it, the Squad finishing it, or it bleeding out) then sends nothing.
    // An opponent revived meanwhile leaves its knockdown open, so it is forgotten after a while.
    // A kill by the Player while a knockdown is open is held for the Kill Marker window: killing an opponent outright
    // wipes its Squad, so when a Kill Badge credit without a Kill Marker follows (the knocked opponent dying in the
    // wipe), the held kill was another opponent and is sent, unless two knockdowns are open: then the Player finished
    // one and the other died in the wipe (2026-11-25 22-28-19 at 701 s). Otherwise it finished the knockdown.
    static readonly TimeSpan OpenKnockdownFor = TimeSpan.FromSeconds(60); // a knocked opponent was finished 34 s later
    readonly List<DateTimeOffset> _openKnockdowns = [];
    DateTimeOffset? _pendingFinish;

    // Death: the spectate label, once per elimination; it may flicker while the camera changes Squad members.
    static readonly TimeSpan DeathRearm = TimeSpan.FromSeconds(5);
    readonly Hysteresis _spectating = new(Cue.Spectating.K);
    bool _dead;
    DateTimeOffset _spectateSeenAt;

    // A Kill Badge is a Kill only shortly after a Kill Marker, and each Kill Marker confirms at most one kill:
    // the Squad finishing the Player's knockdown, or a squad wipe killing it, adds a Kill Badge without one.
    // Measured: 0.4-0.6 s normally, 1.2 s when the Score Popup queues the Kill Badge behind a Knockdown Badge.
    static readonly TimeSpan KillMarkerWindow = TimeSpan.FromSeconds(1.5);
    // The Kill Marker shrinks to about a third while aiming down sights.
    static readonly double[] KillMarkerScales = [1.0, 0.7, 0.5, 0.35];
    bool _killMarkerVisible;

    // Multiplayer: an explosive kill shows no Kill Marker. A Kill Badge that opens an empty Score Popup is a kill on its
    // own there, if the popup was seen empty that long; a badge redrawn shortly after (a ribbon such as "VERGELTUNG"
    // covering it) is not, nor one already showing when watching starts.
    static readonly TimeSpan FreshPopupGap = TimeSpan.FromSeconds(1.5);
    DateTimeOffset? _noKillBadgeSince;
    DateTimeOffset _popupOpenedAt;
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
        var mpHud = (ColourShare(frame.Image, Cue.MpScoreBlue, TeamBlue) >= Cue.MpScoreBlue.Threshold
            && ColourShare(frame.Image, Cue.MpScoreRed, TeamRed) >= Cue.MpScoreRed.Threshold)
            || EdgeScore(frame.Image, Cue.MpScoreBar, _mpScoreBar!) >= Cue.MpScoreBar.Threshold;

        if (_matchId is null)
        {
            var redsecStart = _hud.Update(hud, frame.Timestamp);
            var mpStart = _mpHud.Update(mpHud, frame.Timestamp);
            if ((redsecStart ?? mpStart) is { } startedAt)
            {
                var multiplayer = redsecStart is null;
                _log.LogInformation("Cue {Cue} seen at {At:O}", multiplayer ? "mp_score_bar" : Cue.InMatchHud.Name, startedAt);
                StartMatch(multiplayer);
                _hudSeenAt = frame.Timestamp;
                events.Add(Emit("match_started", startedAt));
                return events;
            }
        }
        else if (EndOfMatch(frame, _multiplayerMatch ? mpHud : hud) is { } ended)
            return ended;

        Kills(frame, events);
        if (Redsec)
        {
            Knockdowns(frame, events);
            Death(frame, events);
        }
        return events;
    }

    void Knockdowns(Frame frame, List<GameEvent> events)
    {
        var badges = CountBadges(frame.Image, Cue.KnockdownBadge, _knockdownBadge);
        if (badges == 0) _knockdownBadgesSeenEmpty = true;
        if (_knockdownBadges.Update(badges, frame.Timestamp) is { } knock)
        {
            _log.LogInformation("Cue {Cue} seen: {Count} in Score Popup at {At:O}", Cue.KnockdownBadge.Name, knock.Count, knock.At);
            var marker = _killMarkers.FindLastIndex(m => knock.At - m <= KillMarkerWindow && m != _pendingKnockdown?.KillMarker && m != _pendingFlash?.KillMarker);
            // Without a Kill Marker it is still the Player's knockdown: its marker is often red for only 2-3 video frames,
            // between two checks. The window then runs from the badge itself. Not when it joins a Kill Badge just shown,
            // nor when it was already showing as the Match started (watching began mid-Match).
            if (marker >= 0 || (_knockdownBadgesSeenEmpty && !(knock.At - _killBadgeAt <= KillMarkerWindow)))
            {
                if (_pendingKnockdown is { } older) EmitKnockdown(older, events);
                _pendingKnockdown = (knock.At, marker >= 0 ? _killMarkers[marker] : knock.At);
            }
        }
        if (_pendingKnockdown is { } pending && frame.Timestamp > pending.KillMarker + KillMarkerWindow)
            EmitKnockdown(pending, events);
    }

    void Death(Frame frame, List<GameEvent> events)
    {
        if (_spectate is null) return; // no spectate label for this game language
        var seen = GrayScore(frame.Image, Cue.Spectating, _spectate) >= Cue.Spectating.Threshold;
        if (seen) _spectateSeenAt = frame.Timestamp;
        if (_spectating.Update(seen, frame.Timestamp) is { } diedAt && !_dead)
        {
            _log.LogInformation("Cue {Cue} seen at {At:O}", Cue.Spectating.Name, diedAt);
            _dead = true;
            events.Add(Emit("death", diedAt));
        }
        if (_dead && !seen && frame.Timestamp - _spectateSeenAt > DeathRearm) _dead = false;
    }

    List<GameEvent>? EndOfMatch(Frame frame, bool hud)
    {
        if (hud) _hudSeenAt = frame.Timestamp;
        var victory = _victory.Update(_multiplayerMatch
            ? MaskScore(frame.Image, Cue.MpEndShield, TeamBlue, _mpEndEmblems) >= Cue.MpEndShield.Threshold
            : MaskScore(frame.Image, Cue.EndVictory, VictoryGold, _endVictory) >= Cue.EndVictory.Threshold, frame.Timestamp);
        var defeat = _defeat.Update(_multiplayerMatch
            ? MaskScore(frame.Image, Cue.MpEndShield, TeamRed, _mpEndEmblems) >= Cue.MpEndShield.Threshold
            : MaskScore(frame.Image, Cue.EndDefeat, DefeatRed, _endDefeat) >= Cue.EndDefeat.Threshold, frame.Timestamp);
        if ((victory ?? defeat) is { } endedAt)
        {
            _log.LogInformation("Cue {Cue} seen at {At:O}", _multiplayerMatch ? (victory is null ? "mp_end_red_emblem" : "mp_end_blue_emblem") : (victory is null ? Cue.EndDefeat : Cue.EndVictory).Name, endedAt);
            return EndMatch(endedAt, victory is null ? "defeat" : "victory", "end_screen");
        }
        if (frame.Timestamp - _hudSeenAt > _hudLostAfter)
        {
            _log.LogInformation("Cue {Cue} absent since {At:O}", _multiplayerMatch ? "mp_score_bar" : Cue.InMatchHud.Name, _hudSeenAt);
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

        var multiplayer = !Redsec;
        var badge = multiplayer ? Cue.KillBadgePlain : Cue.KillBadge;
        var badges = CountBadges(frame.Image, badge, multiplayer ? _killBadgePlain : _killBadge);
        if (badges == 0) _noKillBadgeSince ??= frame.Timestamp;
        else
        {
            if (frame.Timestamp - _noKillBadgeSince >= FreshPopupGap) _popupOpenedAt = frame.Timestamp;
            _noKillBadgeSince = null;
        }
        if (_killBadges.Update(badges, frame.Timestamp, rearmAfterDip: multiplayer) is { } rise)
        {
            var at = rise.At;
            _killBadgeAt = at;
            _log.LogInformation("Cue {Cue} seen: {Count} in Score Popup at {At:O}", badge.Name, rise.Count, at);
            var marker = _killMarkers.FindLastIndex(m => at - m <= KillMarkerWindow);
            if (marker >= 0 || _killMarkerBands is null || (multiplayer && at == _popupOpenedAt))
            {
                DateTimeOffset? markerAt = marker >= 0 ? _killMarkers[marker] : null;
                if (_pendingKnockdown is { } knock)
                {
                    if (knock.KillMarker == markerAt) _pendingKnockdown = null; // queued credit, not a knockdown of its own
                    else EmitKnockdown(knock, events); // an earlier knockdown of the opponent now finished
                }
                ConfirmedKill(at, events);
                if (markerAt is { } used) _killMarkers.Remove(used);
                _pendingFlash = null;
                _badgeKillAt = at;
            }
            else
            {
                if (_pendingFinish is { } held)
                {
                    if (_openKnockdowns.Count >= 2) FinishKnockdown(held);
                    else events.Add(EmitKill(held));
                    _pendingFinish = null;
                }
                if (_openKnockdowns.Count > 0) _openKnockdowns.RemoveAt(0);
                Unconfirmed?.Invoke("kill_badge_without_kill_marker", at);
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
            EmitPendingFlash(flash, events);
        if (_pendingFinish is { } finish && frame.Timestamp > finish + KillMarkerWindow) FinishKnockdown(finish);
        _openKnockdowns.RemoveAll(k => frame.Timestamp - k > OpenKnockdownFor);

        foreach (var expired in _killMarkers.Where(m => frame.Timestamp - m > KillMarkerWindow && _pendingFlash?.KillMarker != m && _pendingKnockdown?.KillMarker != m).ToList())
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

    void StartMatch(bool multiplayer)
    {
        _matchId = Guid.NewGuid().ToString("N");
        _multiplayerMatch = multiplayer;
        _victory.Reset();
        _defeat.Reset();
        // A multiplayer round keeps the Kill Badge rules used before it, so what the Score Popup and the Kill Marker
        // showed while its start was being confirmed still counts; only the counts start again.
        if (multiplayer) (_sequence, _kills) = (0, 0);
        else ResetKills();
    }

    // Per Match, and back to multiplayer kills (other Kill Badge template) after it.
    void ResetKills()
    {
        _sequence = 0;
        _kills = 0;
        _killBadges.Reset();
        _knockdownBadges.Reset();
        _pendingKnockdown = null;
        _spectating.Reset();
        _dead = false;
        _killMarkerVisible = false;
        _noKillBadgeSince = null;
        _killMarkers.Clear();
        _flash.Reset();
        _pendingFlash = null;
        _badgeKillAt = null;
        _killBadgeAt = null;
        _knockdownBadgesSeenEmpty = false;
        _openKnockdowns.Clear();
        _pendingFinish = null;
    }

    // A held knockdown or kill counter flash belongs to the Match that ends, so it is sent before match_ended.
    List<GameEvent> EndMatch(DateTimeOffset at, string result, string reason)
    {
        List<GameEvent> events = [];
        if (_pendingKnockdown is { } knock) EmitKnockdown(knock, events);
        if (_pendingFlash is { } flash) EmitPendingFlash(flash, events);
        if (_pendingFinish is { } finish) FinishKnockdown(finish);
        events.Add(Emit("match_ended", at, new() { ["result"] = result, ["reason"] = reason, ["kills"] = Count(_kills) }));
        _matchId = null;
        _hud.Reset();
        _mpHud.Reset();
        ResetKills();
        return events;
    }

    void EmitPendingFlash((DateTimeOffset At, DateTimeOffset KillMarker) flash, List<GameEvent> events)
    {
        _pendingFlash = null;
        _killMarkers.Remove(flash.KillMarker);
        ConfirmedKill(flash.At, events);
    }

    void ConfirmedKill(DateTimeOffset at, List<GameEvent> events)
    {
        if (_pendingFinish is { } older) FinishKnockdown(older);
        if (_openKnockdowns.Count > 0) _pendingFinish = at;
        else events.Add(EmitKill(at));
    }

    // The kill of an opponent the Player knocked down was already sent with the knockdown.
    void FinishKnockdown(DateTimeOffset at)
    {
        _pendingFinish = null;
        if (_openKnockdowns.Count == 0) return;
        _log.LogInformation("Kill at {At:O} finishes the knockdown at {Knockdown:O}, already sent as a kill", at, _openKnockdowns[0]);
        _openKnockdowns.RemoveAt(0);
    }

    GameEvent EmitKill(DateTimeOffset at) => Emit("kill", at, new() { ["kills"] = Count(++_kills) });

    void EmitKnockdown((DateTimeOffset At, DateTimeOffset KillMarker) knock, List<GameEvent> events)
    {
        _pendingKnockdown = null;
        _killMarkers.Remove(knock.KillMarker);
        _openKnockdowns.Add(knock.At);
        _log.LogInformation("Knockdown at {At:O}", knock.At);
        events.Add(Emit("knockdown", knock.At));
        events.Add(EmitKill(knock.At));
    }

    static string Count(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Templates are scaled to the frame size, so they are (re)loaded when it changes.
    void LoadTemplates(Size frame)
    {
        _frameSize = frame;
        _killBadge = Cue.KillBadge.LoadTemplate(frame, _gameLanguage);
        _killBadgePlain = Cue.KillBadgePlain.LoadTemplate(frame, _gameLanguage);
        _knockdownBadge = Cue.KnockdownBadge.LoadTemplate(frame, _gameLanguage);
        _spectate = Cue.Spectating.HasTemplate(_gameLanguage) ? Cue.Spectating.LoadTemplate(frame, _gameLanguage) : null;
        using var killMarker = Cue.KillMarker.LoadTemplate(frame, _gameLanguage);
        foreach (var scaled in _killMarker) scaled.Dispose();
        _killMarker = KillMarkerScales.Select(s => killMarker.Resize(new Size(Math.Round(killMarker.Width * s), Math.Round(killMarker.Height * s)), 0, 0, InterpolationFlags.Area)).ToArray();
        if (_useKillCounterFlash) _killCounter = Cue.KillCounterFlash.LoadTemplate(frame, _gameLanguage);
        _inMatchHud = Cue.InMatchHud.LoadTemplate(frame, _gameLanguage);
        _endVictory = Cue.EndVictory.LoadTemplate(frame, _gameLanguage);
        _endDefeat = Cue.EndDefeat.LoadTemplate(frame, _gameLanguage);
        foreach (var emblem in _mpEndEmblems) emblem.Dispose();
        _mpEndEmblems = [.. new[] { Cue.MpEndShield, Cue.MpEndHexagon, Cue.MpEndFlag, Cue.MpEndBurst }.Select(c => c.LoadTemplate(frame, _gameLanguage))];
        _mpScoreBar?.Dispose();
        using var bar = Cue.MpScoreBar.LoadTemplate(new Size(1280, 720), _gameLanguage); // scored at a fixed size, whatever the frame size
        _mpScoreBar = bar.Threshold(0, 255, ThresholdTypes.Binary);
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

    // Mean edge strength on the template's pixels over the mean of the whole region, the region scaled to the template.
    // High when a translucent outline is drawn over any background.
    static double EdgeScore(Mat image, Cue cue, Mat template)
    {
        using var region = cue.Crop(image);
        using var gray = region.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var small = gray.Resize(template.Size(), 0, 0, InterpolationFlags.Area);
        using var blurred = new Mat();
        small.ConvertTo(blurred, MatType.CV_32F);
        Cv2.GaussianBlur(blurred, blurred, new Size(3, 3), 0);
        using var dx = blurred.Sobel(MatType.CV_32F, 1, 0);
        using var dy = blurred.Sobel(MatType.CV_32F, 0, 1);
        using var magnitude = new Mat();
        Cv2.Magnitude(dx, dy, magnitude);
        return Cv2.Mean(magnitude, template).Val0 / (Cv2.Mean(magnitude).Val0 + 1e-3);
    }

    // The share of a Cue region's pixels that fall in one of the HSV bands.
    static double ColourShare(Mat image, Cue cue, (Scalar Low, Scalar High)[] bands)
    {
        using var region = cue.Crop(image);
        using var hsv = region.CvtColor(ColorConversionCodes.BGR2HSV);
        var inside = 0;
        foreach (var (low, high) in bands)
        {
            using var band = hsv.InRange(low, high);
            inside += band.CountNonZero();
        }
        return (double)inside / (hsv.Rows * hsv.Cols);
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

    static int CountBadges(Mat image, Cue cue, Mat badge)
    {
        using var region = cue.Crop(image);
        using var gray = region.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var scores = gray.MatchTemplate(badge, TemplateMatchModes.CCoeffNormed);

        // Count every match above the threshold, blanking each one's neighbourhood so it is counted once.
        var count = 0;
        while (true)
        {
            scores.MinMaxLoc(out _, out double max, out _, out Point at);
            if (max < cue.Threshold) return count;
            count++;
            scores.Rectangle(new Rect(at.X - badge.Width, at.Y - badge.Height, 2 * badge.Width, 2 * badge.Height), Scalar.All(-1), -1);
        }
    }

    /// <summary>Badges of one kind confirmed in the Score Popup. More must show for K checks; fewer must persist
    /// <see cref="KillBadgeReleaseChecks"/> checks, so a badge missed on a bright background is not counted again.</summary>
    sealed class BadgeTally(int k)
    {
        int _count;
        readonly List<(DateTimeOffset At, int Count)> _rise = [];
        readonly List<int> _fall = [];

        /// <summary>The time of the first check of a confirmed rise and the new count, once.</summary>
        /// <param name="rearmAfterDip">The same count returning after a short dip is a rise too: in multiplayer the Score
        /// Popup redraws its one skull in place for a further kill (multi-kill), hiding it for a few checks.</param>
        public (DateTimeOffset At, int Count)? Update(int count, DateTimeOffset at, bool rearmAfterDip = false)
        {
            if (count > _count || (rearmAfterDip && _dipped && count == _count && count > 0))
            {
                _fall.Clear();
                _rise.Add((at, count));
                if (_rise.Count < k) return null;
                var confirmed = (_rise[0].At, _rise.Min(r => r.Count));
                _count = confirmed.Item2;
                _rise.Clear();
                _dipped = false;
                return confirmed;
            }
            _rise.Clear();
            if (count == _count)
            {
                _fall.Clear();
                _dipped = false;
            }
            else
            {
                _dipped = true;
                _fall.Add(count);
                if (_fall.Count >= KillBadgeReleaseChecks)
                {
                    _count = _fall.Max();
                    _fall.Clear();
                    _dipped = false;
                }
            }
            return null;
        }

        bool _dipped;

        public void Reset()
        {
            _count = 0;
            _rise.Clear();
            _fall.Clear();
            _dipped = false;
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
