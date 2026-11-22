using OpenCvSharp;

namespace MatchPulse.Tests;

// Synthetic frame sequences: a plain 2560x1440 frame with badge templates pasted into the Score Popup.
public class KillDetectionTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(1));
    static readonly string Cues = Path.Combine(AppContext.BaseDirectory, "cues");

    [Fact]
    public void Kill_Badge_held_across_many_checks_yields_one_kill_at_first_sight()
    {
        var events = Run(Blank(3), WithBadges(20, "kill_badge"), Blank(5));

        var kill = Assert.Single(events);
        Assert.Equal("kill", kill.Type);
        Assert.Equal(1, kill.Sequence);
        Assert.Equal(Start.AddSeconds(0.3), kill.OccurredAt);
    }

    [Fact]
    public void Kill_Badge_flickering_for_a_single_check_yields_nothing()
    {
        Assert.Empty(Run(Blank(3), WithBadges(1, "kill_badge"), Blank(5)));
    }

    [Fact]
    public void Knockdown_Badge_yields_no_kill()
    {
        Assert.Empty(Run(Blank(3), WithBadges(20, "knockdown_badge"), Blank(5)));
    }

    [Fact]
    public void Kill_Badge_added_next_to_a_Knockdown_Badge_yields_one_kill()
    {
        var events = Run(WithBadges(10, "knockdown_badge"), WithBadges(10, "knockdown_badge", "kill_badge"));

        Assert.Equal(Start.AddSeconds(1.0), Assert.Single(events).OccurredAt);
    }

    [Fact]
    public void Second_Kill_Badge_added_to_the_Score_Popup_yields_a_second_kill()
    {
        var events = Run(WithBadges(10, "kill_badge"), WithBadges(10, "kill_badge", "kill_badge"));

        Assert.Equal([1, 2], events.Select(e => e.Sequence));
        Assert.Equal([Start, Start.AddSeconds(1.0)], events.Select(e => e.OccurredAt));
        Assert.Single(events.Select(e => e.MatchId).Distinct());
    }

    [Fact]
    public void Kill_Badge_in_a_new_Score_Popup_yields_a_new_kill()
    {
        var events = Run(WithBadges(10, "kill_badge"), Blank(20), WithBadges(10, "kill_badge"));

        Assert.Equal([Start, Start.AddSeconds(3.0)], events.Select(e => e.OccurredAt));
    }

    [Fact]
    public void Kill_Badge_missed_for_two_checks_is_not_counted_again()
    {
        var events = Run(WithBadges(10, "kill_badge"), Blank(2), WithBadges(10, "kill_badge"));

        Assert.Single(events);
    }

    static List<GameEvent> Run(params Mat[][] segments)
    {
        var detector = new MatchDetector();
        var events = new List<GameEvent>();
        var i = 0;
        foreach (var image in segments.SelectMany(s => s))
            events.AddRange(detector.Process(new Frame(Start.AddSeconds(i++ * 0.1), image)));
        return events;
    }

    static Mat[] Blank(int checks) => Enumerable.Repeat(BlankFrame(), checks).ToArray();

    // Badges are placed left to right in the Score Popup, as the game lays them out.
    static Mat[] WithBadges(int checks, params string[] badges)
    {
        var frame = BlankFrame();
        var x = 900;
        foreach (var badge in badges)
        {
            using var template = Cv2.ImRead(Path.Combine(Cues, badge + ".png"));
            template.CopyTo(frame[new Rect(x, 920, template.Width, template.Height)]);
            x += 48;
        }
        return Enumerable.Repeat(frame, checks).ToArray();
    }

    static Mat BlankFrame() => new(1440, 2560, MatType.CV_8UC3, new Scalar(40, 40, 40));
}
