using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Diagnostic mode: keeps the last 2.5 s of the screen centre (crosshair and Score Popup) and the Kill Feed, and
/// when the Match Detector reports an unconfirmed case, writes them as one contact sheet JPEG to <paramref name="folder"/>.
/// </summary>
public sealed class DiagnosticSnapshots(string folder, ILogger log) : IDisposable
{
    /// <summary>Screen areas captured live in addition to the Cue regions.</summary>
    public static readonly Rect2d Centre = new(0.25, 0.40, 0.50, 0.36);
    public static readonly Rect2d KillFeed = new(0.70, 0.02, 0.30, 0.16);
    public static readonly Rect2d[] Regions = [Centre, KillFeed];

    public static readonly string DefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatchPulse", "diagnostics");

    static readonly TimeSpan Keep = TimeSpan.FromSeconds(2.5);
    const int Columns = 5;
    readonly Queue<(DateTimeOffset At, Mat Centre, Mat KillFeed)> _frames = new();

    public void Add(Frame frame)
    {
        _frames.Enqueue((frame.Timestamp, Crop(frame.Image, Centre, 0.5), Crop(frame.Image, KillFeed, 1)));
        while (frame.Timestamp - _frames.Peek().At > Keep) Release(_frames.Dequeue());
    }

    public void Write(string what, DateTimeOffset at)
    {
        if (_frames.Count == 0) return;
        try
        {
            var (_, _, killFeed) = _frames.Last();
            var tile = _frames.Peek().Centre.Size();
            var rows = (_frames.Count + Columns - 1) / Columns;
            using var sheet = new Mat(killFeed.Height + rows * tile.Height, Columns * tile.Width, MatType.CV_8UC3, Scalar.Black);
            killFeed.CopyTo(sheet[new Rect(0, 0, killFeed.Width, killFeed.Height)]);
            Cv2.PutText(sheet, $"{what} at {at:HH:mm:ss.fff}", new Point(killFeed.Width + 20, 40), HersheyFonts.HersheySimplex, 1, Scalar.White, 2);
            foreach (var ((t, centre, _), i) in _frames.Select((f, i) => (f, i)))
            {
                var cell = sheet[new Rect(i % Columns * tile.Width, killFeed.Height + i / Columns * tile.Height, tile.Width, tile.Height)];
                centre.CopyTo(cell);
                Cv2.PutText(cell, $"{(t - at).TotalSeconds:+0.0;-0.0;0.0} s", new Point(8, 28), HersheyFonts.HersheySimplex, 0.8, Scalar.Yellow, 2);
            }
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{at.ToLocalTime():yyyy-MM-dd_HH-mm-ss.fff}_{what}.jpg");
            Cv2.ImWrite(path, sheet, new ImageEncodingParam(ImwriteFlags.JpegQuality, 85));
            log.LogInformation("Diagnostic snapshot {Path}", path);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Diagnostic snapshot for {What} failed", what);
        }
    }

    static Mat Crop(Mat image, Rect2d region, double scale)
    {
        using var crop = new Mat(image, Cue.Pixels(region, image.Size()));
        return crop.Resize(new Size(), scale, scale, InterpolationFlags.Area);
    }

    static void Release((DateTimeOffset, Mat Centre, Mat KillFeed) frame)
    {
        frame.Centre.Dispose();
        frame.KillFeed.Dispose();
    }

    public void Dispose()
    {
        while (_frames.Count > 0) Release(_frames.Dequeue());
    }
}
