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

    public Mat LoadTemplate() =>
        Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "cues", Template), ImreadModes.Grayscale);

    public Mat Crop(Mat frame) => new(frame, new Rect(
        (int)(SearchRegion.X * frame.Width), (int)(SearchRegion.Y * frame.Height),
        (int)(SearchRegion.Width * frame.Width), (int)(SearchRegion.Height * frame.Height)));
}
