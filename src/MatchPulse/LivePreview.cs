using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Live picture of the live monitor: while the window asks for it (<see cref="Ask"/> at least every few seconds), about one
/// full frame per second goes to <see cref="Picture"/> as a small JPEG data URL. Shows what the capture sees, e.g. the
/// desktop instead of the game. Thread-safe.
/// </summary>
public sealed class LivePreview
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(1), AskedFor = TimeSpan.FromSeconds(3);
    const int Width = 960;

    long _askedUntil, _next; // UTC ticks

    /// <summary>A picture: JPEG data URL and capture time.</summary>
    public event Action<string, DateTimeOffset>? Picture;

    public void Ask() => Interlocked.Exchange(ref _askedUntil, (DateTimeOffset.UtcNow + AskedFor).UtcTicks);

    /// <summary>True when the next frame should be full for a picture.</summary>
    public bool Wanted
    {
        get
        {
            var now = DateTimeOffset.UtcNow.UtcTicks;
            return now < Interlocked.Read(ref _askedUntil) && now >= Interlocked.Read(ref _next);
        }
    }

    /// <summary>Sends <paramref name="frame"/> as the picture if it is full and one is wanted.</summary>
    public void Offer(Frame frame)
    {
        if (!frame.Full || !Wanted) return;
        Interlocked.Exchange(ref _next, (DateTimeOffset.UtcNow + Every).UtcTicks);
        var scale = (double)Width / frame.Image.Width;
        using var small = frame.Image.Resize(new Size(), scale, scale, InterpolationFlags.Area);
        Cv2.ImEncode(".jpg", small, out var jpeg, new ImageEncodingParam(ImwriteFlags.JpegQuality, 70));
        Picture?.Invoke("data:image/jpeg;base64," + Convert.ToBase64String(jpeg), frame.Timestamp);
    }
}
