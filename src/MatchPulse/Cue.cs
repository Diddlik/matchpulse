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

    public Mat Crop(Mat frame) => new(frame, Region(frame.Size()));

    /// <summary>The search region in pixels of a frame of the given size.</summary>
    public Rect Region(Size frame) => Pixels(SearchRegion, frame);

    public static Rect Pixels(Rect2d region, Size frame) => new(
        (int)(region.X * frame.Width), (int)(region.Y * frame.Height),
        (int)(region.Width * frame.Width), (int)(region.Height * frame.Height));
}
