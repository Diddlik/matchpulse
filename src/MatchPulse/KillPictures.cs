using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace MatchPulse;

/// <summary>
/// Live monitor: after a <c>kill</c> is requested, the next full frame is saved as a JPEG to <paramref name="folder"/>,
/// named <c>&lt;local time&gt;_&lt;matchId or none&gt;_&lt;sequence&gt;.jpg</c>. Only the newest <see cref="Keep"/> pictures are kept.
/// </summary>
public sealed class KillPictures(string folder, ILogger log)
{
    public static readonly string DefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatchPulse", "kills");

    public const int Keep = 300;
    const int MaxWidth = 1920;
    const string TimeFormat = "yyyy-MM-dd_HH-mm-ss.fff";

    GameEvent? _pending;

    /// <summary>True while a kill waits for its picture: the next frame should be full.</summary>
    public bool Wanted => _pending is not null;

    public void Request(GameEvent kill) => _pending = kill;

    /// <summary>Saves the picture of the requested kill if <paramref name="frame"/> is full; returns the kill and the file.</summary>
    public (GameEvent Kill, string Path)? Save(Frame frame)
    {
        if (_pending is not { } kill || !frame.Full) return null;
        _pending = null;
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName(kill, frame.Timestamp));
            var scale = Math.Min(1.0, (double)MaxWidth / frame.Image.Width);
            using (var picture = scale < 1 ? frame.Image.Resize(new Size(), scale, scale, InterpolationFlags.Area) : frame.Image.Clone())
                Cv2.ImWrite(path, picture, new ImageEncodingParam(ImwriteFlags.JpegQuality, 85));
            foreach (var old in Directory.GetFiles(folder, "*.jpg").OrderDescending().Skip(Keep)) File.Delete(old);
            log.LogInformation("Kill picture {Path}", path);
            return (kill, path);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Kill picture for #{Sequence} failed", kill.Sequence);
            return null;
        }
    }

    public static string FileName(GameEvent kill, DateTimeOffset at) =>
        $"{at.ToString(TimeFormat, CultureInfo.InvariantCulture)}_{kill.MatchId ?? "none"}_{kill.Sequence}.jpg";

    /// <summary>Reads a file name written by <see cref="FileName"/>: capture time, matchId (null outside a Match) and sequence.</summary>
    public static (DateTimeOffset At, string? MatchId, int Sequence)? Parse(string fileName)
    {
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('_');
        if (parts.Length != 4
            || !DateTime.TryParseExact(parts[0] + "_" + parts[1], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)
            || !int.TryParse(parts[3], out var sequence)) return null;
        return (new DateTimeOffset(at), parts[2] == "none" ? null : parts[2], sequence);
    }
}
