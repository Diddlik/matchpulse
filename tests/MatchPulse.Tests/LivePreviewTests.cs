using OpenCvSharp;

namespace MatchPulse.Tests;

public sealed class LivePreviewTests
{
    [Fact]
    public void Sends_full_frames_only_while_asked_and_at_most_once_per_second()
    {
        var preview = new LivePreview();
        var pictures = new List<string>();
        preview.Picture += (url, _) => pictures.Add(url);
        using var image = new Mat(1440, 2560, MatType.CV_8UC3, Scalar.Gray);
        var full = new Frame(DateTimeOffset.Now, image) { Full = true };

        Assert.False(preview.Wanted);
        preview.Offer(full); // not asked
        preview.Ask();
        Assert.True(preview.Wanted);
        preview.Offer(new Frame(DateTimeOffset.Now, image)); // cue regions only
        preview.Offer(full);
        preview.Offer(full); // within a second

        var url = Assert.Single(pictures);
        Assert.StartsWith("data:image/jpeg;base64,", url);
        Assert.False(preview.Wanted);
    }
}
