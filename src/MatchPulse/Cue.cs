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

    /// <summary>Red X around the crosshair, matched as a red mask. Shown on knockdowns too, so it only gates the Kill Badge.</summary>
    public static readonly Cue KillMarker = new("kill_marker", "kill_marker_mask.png", new Rect2d(0.4531, 0.4167, 0.0938, 0.1667), 0.3, 1);

    /// <summary>REDSEC kill counter skull in the counter bar, located by shape; it counts only while it flashes green.</summary>
    public static readonly Cue KillCounterFlash = new("kill_counter_flash", "kill_counter_flash.png", new Rect2d(0.742, 0.042, 0.258, 0.059), 0.7, 2);

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
    public Rect Region(Size frame) => new(
        (int)(SearchRegion.X * frame.Width), (int)(SearchRegion.Y * frame.Height),
        (int)(SearchRegion.Width * frame.Width), (int)(SearchRegion.Height * frame.Height));
}
