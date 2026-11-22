using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Frame Source that decodes a recorded video (MKV or MP4) and samples it by timestamp,
/// so clips at any frame rate yield the same checks. The caller disposes each frame's image.
/// </summary>
public static class ReplayFrameSource
{
    public static IEnumerable<Frame> Read(string path, DateTimeOffset start, double checksPerSecond)
    {
        if (checksPerSecond is < 5 or > 10)
            throw new ArgumentOutOfRangeException(nameof(checksPerSecond), checksPerSecond, "Must be between 5 and 10.");
        if (!File.Exists(path)) throw new FileNotFoundException("Video not found.", path);

        using var capture = new VideoCapture(path);
        if (!capture.IsOpened()) throw new InvalidOperationException($"Cannot decode {path}.");

        var interval = 1000.0 / checksPerSecond;
        var next = 0.0;
        while (capture.Grab())
        {
            var position = capture.PosMsec;
            if (position + 0.5 < next) continue;
            next = Math.Max(next, position) + interval; // after a timestamp gap, resume sampling from here

            var image = new Mat();
            capture.Retrieve(image);
            yield return new Frame(start.AddMilliseconds(position), image);
        }
    }
}
