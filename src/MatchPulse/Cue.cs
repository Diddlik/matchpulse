using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// A Cue definition. <see cref="SearchRegion"/> is relative to the frame (x, y, w, h as fractions).
/// <see cref="Template"/> is a file in the <c>cues</c> folder (copied from <c>fixtures/cues</c>), cut at 2560x1440.
/// The Cue counts as seen after <see cref="K"/> consecutive matching checks.
/// </summary>
public sealed record Cue(string Name, string Template, Rect2d SearchRegion, double Threshold, int K)
{
    /// <summary>Kill Badge in the Score Popup. The Knockdown Badge scores at most ~0.75 against it.</summary>
    public static readonly Cue KillBadge = new("kill_badge", "kill_badge.png", new Rect2d(0.30, 0.62, 0.20, 0.06), 0.8, 2);

    /// <summary>Red X around the crosshair, matched as a red mask. Shown on knockdowns too, so it only gates the Kill Badge.</summary>
    public static readonly Cue KillMarker = new("kill_marker", "kill_marker_mask.png", new Rect2d(0.4531, 0.4167, 0.0938, 0.1667), 0.3, 1);

    /// <summary>REDSEC kill counter skull in the counter bar, located by shape; it counts only while it flashes green.</summary>
    public static readonly Cue KillCounterFlash = new("kill_counter_flash", "kill_counter_flash.png", new Rect2d(0.742, 0.042, 0.258, 0.059), 0.7, 2);

    /// <summary>Squads icon at the left of the counter bar. The bar is right-aligned and grows, so the icon is searched in a band.</summary>
    public static readonly Cue InMatchHud = new("in_match_hud", "in_match_hud.png", new Rect2d(0.742, 0.042, 0.258, 0.059), 0.7, 5);

    /// <summary>Gold laurel emblem of the victory screen, matched as a gold mask.</summary>
    public static readonly Cue EndVictory = new("end_victory", "end_victory_mask.png", new Rect2d(0.4219, 0.1979, 0.1563, 0.2222), 0.8, 3);

    /// <summary>Red shield emblem of the defeat screen, matched as a red mask. It shows for only about 2 s.</summary>
    public static readonly Cue EndDefeat = new("end_defeat", "end_defeat_mask.png", new Rect2d(0.4375, 0.1528, 0.1250, 0.2778), 0.8, 3);

    public Mat LoadTemplate() =>
        Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "cues", Template), ImreadModes.Grayscale);

    public Mat Crop(Mat frame) => new(frame, Region(frame.Size()));

    /// <summary>The search region in pixels of a frame of the given size.</summary>
    public Rect Region(Size frame) => new(
        (int)(SearchRegion.X * frame.Width), (int)(SearchRegion.Y * frame.Height),
        (int)(SearchRegion.Width * frame.Width), (int)(SearchRegion.Height * frame.Height));
}
