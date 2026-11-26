using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// A Cue definition. <see cref="SearchRegion"/> is relative to the frame (x, y, w, h as fractions).
/// <see cref="Template"/> is a file in the <c>cues</c> folder (copied from <c>fixtures/cues</c>), cut at 2560x1440.
/// A text Cue puts its per-language templates in <c>cues/&lt;language&gt;/</c>; a language-neutral Cue has one template in <c>cues/</c>.
/// The Cue counts as seen after <see cref="K"/> consecutive matching checks.
/// </summary>
public sealed record Cue(string Name, string Template, Rect2d SearchRegion, double Threshold, int K)
{
    /// <summary>Kill Badge in the Score Popup. The Knockdown Badge scores at most ~0.75 against it.</summary>
    public static readonly Cue KillBadge = new("kill_badge", "kill_badge.png", new Rect2d(0.30, 0.62, 0.20, 0.06), 0.8, 2);

    /// <summary>Kill Badge outside REDSEC (multiplayer): a skull without the tick, scoring 0.96-0.99 on own kills.
    /// It also matches the REDSEC Knockdown Badge, so it is used only while no Match runs.</summary>
    public static readonly Cue KillBadgePlain = new("kill_badge_plain", "kill_badge_plain.png", new Rect2d(0.30, 0.62, 0.20, 0.06), 0.8, 2);

    /// <summary>Knockdown Badge (skull with a down arrow) in the Score Popup, REDSEC. The Kill Badge scores at most ~0.75 against it;
    /// in multiplayer the plain kill skull scores ~0.87, so it is used only inside a Match.</summary>
    public static readonly Cue KnockdownBadge = new("knockdown_badge", "knockdown_badge.png", new Rect2d(0.30, 0.62, 0.20, 0.06), 0.8, 2);

    /// <summary>"BEOBACHTE" label while the Player spectates after being eliminated (REDSEC). A text Cue: one template per game language.</summary>
    public static readonly Cue Spectating = new("spectating", "spectate_label.png", new Rect2d(0.414, 0.722, 0.172, 0.083), 0.7, 3);

    /// <summary>X around the crosshair (red by default, colour set in the game), matched as a colour mask at 4 scales.
    /// Shown on knockdowns too, so it only gates the Kill Badge. Kill Markers score 0.43-0.71 on the fixtures, other frames at most 0.30.</summary>
    public static readonly Cue KillMarker = new("kill_marker", "kill_marker_mask.png", new Rect2d(0.4531, 0.4167, 0.0938, 0.1667), 0.37, 1);

    /// <summary>REDSEC kill counter skull in the counter bar, located by shape; it counts only while it flashes green.</summary>
    public static readonly Cue KillCounterFlash = new("kill_counter_flash", "kill_counter_flash.png", new Rect2d(0.742, 0.042, 0.258, 0.059), 0.7, 2);

    /// <summary>Squads icon at the left of the counter bar. The bar is right-aligned and grows, so the icon is searched in a band.</summary>
    public static readonly Cue InMatchHud = new("in_match_hud", "in_match_hud.png", new Rect2d(0.742, 0.042, 0.258, 0.059), 0.7, 5);

    /// <summary>Gold laurel emblem of the victory screen, matched as a gold mask.</summary>
    public static readonly Cue EndVictory = new("end_victory", "end_victory_mask.png", new Rect2d(0.4219, 0.1979, 0.1563, 0.2222), 0.8, 3);

    /// <summary>Red shield emblem of the defeat screen, matched as a red mask. It shows for only about 2 s.</summary>
    public static readonly Cue EndDefeat = new("end_defeat", "end_defeat_mask.png", new Rect2d(0.4375, 0.1528, 0.1250, 0.2778), 0.8, 3);

    /// <summary>Multiplayer: the Player's team score box at the left of the score bar (top centre). Its score changes, so it is
    /// matched as the share of team-blue pixels (<see cref="Threshold"/>), not by a template; with <see cref="MpScoreRed"/> it is the multiplayer in-match HUD.</summary>
    public static readonly Cue MpScoreBlue = new("mp_score_blue", "", new Rect2d(0.3566, 0.0507, 0.0430, 0.0278), 0.06, 5);

    /// <summary>Multiplayer: the enemy team score box at the right of the score bar, matched as the share of team-red pixels.</summary>
    public static readonly Cue MpScoreRed = new("mp_score_red", "", new Rect2d(0.6000, 0.0507, 0.0438, 0.0278), 0.06, 5);

    /// <summary>Multiplayer: emblem of the banner at the end of a round, 2-3 s long. Its shape depends on the mode (Conquest: shield,
    /// Team Deathmatch: hexagon with a skull), its colour on the result (blue "SIEG", red "NIEDERLAGE"), so both shapes are
    /// matched as a blue and as a red mask. Banners score 0.95-0.97; other frames at most 0.59 (REDSEC) and 0.55 (rounds).</summary>
    public static readonly Cue MpEndShield = new("mp_end_shield", "mp_end_shield_mask.png", new Rect2d(0.4219, 0.1389, 0.1563, 0.2778), 0.8, 3);

    /// <inheritdoc cref="MpEndShield"/>
    public static readonly Cue MpEndHexagon = new("mp_end_hexagon", "mp_end_hexagon_mask.png", new Rect2d(0.4219, 0.1389, 0.1563, 0.2778), 0.8, 3);

    public const int ReferenceHeight = 1440;

    /// <summary>The template for <paramref name="language"/>, scaled from 2560x1440 to a 16:9 frame of the given size.</summary>
    public Mat LoadTemplate(Size frame, string language)
    {
        var cues = Path.Combine(AppContext.BaseDirectory, "cues");
        var path = Path.Combine(cues, language, Template);
        using var template = Cv2.ImRead(File.Exists(path) ? path : Path.Combine(cues, Template), ImreadModes.Grayscale);
        var scale = (double)frame.Height / ReferenceHeight;
        return template.Resize(new Size(Math.Round(template.Width * scale), Math.Round(template.Height * scale)), 0, 0, InterpolationFlags.Area);
    }

    /// <summary>True when a template exists for <paramref name="language"/> (or a language-neutral one).</summary>
    public bool HasTemplate(string language)
    {
        var cues = Path.Combine(AppContext.BaseDirectory, "cues");
        return File.Exists(Path.Combine(cues, language, Template)) || File.Exists(Path.Combine(cues, Template));
    }

    public Mat Crop(Mat frame) => new(frame, Region(frame.Size()));

    /// <summary>The search region in pixels of a frame of the given size.</summary>
    public Rect Region(Size frame) => Pixels(SearchRegion, frame);

    public static Rect Pixels(Rect2d region, Size frame) => new(
        (int)(region.X * frame.Width), (int)(region.Y * frame.Height),
        (int)(region.Width * frame.Width), (int)(region.Height * frame.Height));
}
