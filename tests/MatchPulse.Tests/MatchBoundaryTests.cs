using OpenCvSharp;

namespace MatchPulse.Tests;

// Synthetic frame sequences at 1 check per second: a plain 2560x1440 frame with or without the in-match HUD.
public class MatchBoundaryTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(1));
    static readonly string Cues = Path.Combine(AppContext.BaseDirectory, "cues");

    [Fact]
    public void Game_exit_during_a_Match_yields_match_ended_with_reason_game_exited()
    {
        var detector = new MatchDetector();
        Run(detector, Hud(10));

        var ended = Assert.Single(detector.GameExited(Start.AddSeconds(15)));

        Assert.Equal("match_ended", ended.Type);
        Assert.Equal(2, ended.Sequence);
        Assert.Equal(Start.AddSeconds(15), ended.OccurredAt);
        Assert.Equal(new Dictionary<string, string> { ["result"] = "unknown", ["reason"] = "game_exited" }, ended.Data);
    }

    [Fact]
    public void Game_exit_without_a_Match_yields_nothing()
    {
        var detector = new MatchDetector();
        Run(detector, NoHud(10));

        Assert.Empty(detector.GameExited(Start.AddSeconds(10)));
    }

    [Fact]
    public void In_match_HUD_absent_longer_than_the_configured_time_yields_match_ended_hud_lost()
    {
        var events = Run(new MatchDetector(hudLostAfter: TimeSpan.FromSeconds(120)), Hud(10), NoHud(130));

        Assert.Equal(["match_started", "match_ended"], events.Select(e => e.Type));
        Assert.Equal(Start, events[0].OccurredAt);
        Assert.Equal(Start.AddSeconds(130), events[1].OccurredAt); // HUD last seen at 9 s
        Assert.Equal(new Dictionary<string, string> { ["result"] = "unknown", ["reason"] = "hud_lost" }, events[1].Data);
        Assert.Equal([1, 2], events.Select(e => e.Sequence));
    }

    [Fact]
    public void In_match_HUD_absent_for_a_Jammer_does_not_end_the_Match()
    {
        var events = Run(new MatchDetector(), Hud(10), NoHud(60), Hud(10), NoHud(60));

        Assert.Equal(["match_started"], events.Select(e => e.Type));
    }

    [Fact]
    public void In_match_HUD_back_after_hud_lost_starts_a_new_Match()
    {
        var events = Run(new MatchDetector(hudLostAfter: TimeSpan.FromSeconds(30)), Hud(10), NoHud(40), Hud(10));

        Assert.Equal(["match_started", "match_ended", "match_started"], events.Select(e => e.Type));
        Assert.Equal(2, events.Select(e => e.MatchId).Distinct().Count());
        Assert.Equal(1, events[2].Sequence);
    }

    [Fact]
    public void Kill_outside_a_Match_is_not_reported()
    {
        var detector = new MatchDetector();
        var frame = Frame(hud: false);
        using (var marker = Cv2.ImRead(Path.Combine(Cues, "kill_marker.png")))
            marker.CopyTo(frame[new Rect(1180, 620, marker.Width, marker.Height)]);
        using (var badge = Cv2.ImRead(Path.Combine(Cues, "kill_badge.png")))
            badge.CopyTo(frame[new Rect(900, 920, badge.Width, badge.Height)]);

        // At 10 checks per second: Kill Marker and Kill Badge together, held for a second.
        var events = Enumerable.Range(0, 10).SelectMany(i => detector.Process(new Frame(Start.AddMilliseconds(i * 100), frame)));

        Assert.Empty(events);
    }

    static List<GameEvent> Run(MatchDetector detector, params Mat[][] segments)
    {
        var i = 0;
        return segments.SelectMany(s => s).SelectMany(image => detector.Process(new Frame(Start.AddSeconds(i++), image))).ToList();
    }

    static Mat[] Hud(int checks) => Enumerable.Repeat(Frame(hud: true), checks).ToArray();

    static Mat[] NoHud(int checks) => Enumerable.Repeat(Frame(hud: false), checks).ToArray();

    static Mat Frame(bool hud)
    {
        var frame = new Mat(1440, 2560, MatType.CV_8UC3, new Scalar(40, 40, 40));
        if (hud)
        {
            using var template = Cv2.ImRead(Path.Combine(Cues, "in_match_hud.png"));
            template.CopyTo(frame[new Rect(2190, 80, template.Width, template.Height)]);
        }
        return frame;
    }
}
