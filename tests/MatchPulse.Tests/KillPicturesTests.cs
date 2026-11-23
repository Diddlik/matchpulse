using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace MatchPulse.Tests;

public sealed class KillPicturesTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 23, 20, 0, 0, TimeSpan.FromHours(1));
    readonly string _folder = Path.Combine(Path.GetTempPath(), "mp-kills-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Requested_kill_is_saved_from_the_next_full_frame_only()
    {
        var pictures = new KillPictures(_folder, NullLogger.Instance);
        using var image = new Mat(1440, 2560, MatType.CV_8UC3, Scalar.Gray);
        var kill = new GameEvent("kill", null, 3, Start, new Dictionary<string, string>());

        Assert.Null(pictures.Save(new Frame(Start, image) { Full = true })); // nothing requested
        pictures.Request(kill);
        Assert.True(pictures.Wanted);
        Assert.Null(pictures.Save(new Frame(Start, image))); // cue regions only
        var saved = pictures.Save(new Frame(Start.AddSeconds(0.1), image) { Full = true });

        Assert.False(pictures.Wanted);
        Assert.Equal(kill, saved!.Value.Kill);
        using var written = Cv2.ImRead(saved.Value.Path);
        Assert.Equal(1920, written.Width);
        Assert.Equal((Start.AddSeconds(0.1), (string?)null, 3), KillPictures.Parse(Path.GetFileName(saved.Value.Path)));
    }
}
