using System.Diagnostics;
using System.Drawing;

namespace MatchPulse.Tests;

public sealed class CompositionNudgeTests
{
    [Fact]
    public void Opens_and_closes_its_window_quickly()
    {
        var time = Stopwatch.StartNew();
        using (new CompositionNudge(new Rectangle(0, 0, 1920, 1080))) { }
        Assert.True(time.Elapsed < TimeSpan.FromSeconds(2), $"took {time.Elapsed}");
    }
}
