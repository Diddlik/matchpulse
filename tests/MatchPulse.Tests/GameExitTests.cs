using OpenCvSharp;

namespace MatchPulse.Tests;

// Until ticket 04, a Match runs from the detector's first frame.
public class GameExitTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Game_exit_during_a_Match_yields_match_ended_with_reason_game_exited()
    {
        var detector = new MatchDetector();
        using var blank = new Mat(1440, 2560, MatType.CV_8UC3, new Scalar(40, 40, 40));
        Assert.Empty(detector.Process(new Frame(Start, blank)));

        var ended = Assert.Single(detector.GameExited(Start.AddSeconds(5)));

        Assert.Equal("match_ended", ended.Type);
        Assert.Equal(1, ended.Sequence);
        Assert.Equal(Start.AddSeconds(5), ended.OccurredAt);
        Assert.Equal(new Dictionary<string, string> { ["result"] = "unknown", ["reason"] = "game_exited" }, ended.Data);
    }

    [Fact]
    public void Game_exit_without_a_Match_yields_nothing()
    {
        Assert.Empty(new MatchDetector().GameExited(Start));
    }
}
