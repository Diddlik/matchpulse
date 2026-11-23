using OpenCvSharp;

namespace MatchPulse.Tests;

// Synthetic frame sequences: a plain 2560x1440 frame showing the in-match HUD, with badge templates pasted into the Score Popup.
// Each sequence runs inside a Match that started a second before it.
public class KillDetectionTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(1));
    static readonly string Cues = Path.Combine(AppContext.BaseDirectory, "cues");

    [Fact]
    public void Kill_Badge_held_across_many_checks_yields_one_kill_at_first_sight()
    {
        var events = Run(Blank(3), KillMarker(2), WithBadges(20, "kill_badge"), Blank(5));

        var kill = Assert.Single(events);
        Assert.Equal("kill", kill.Type);
        Assert.Equal(2, kill.Sequence);
        Assert.Equal(Start.AddMilliseconds(500), kill.OccurredAt);
    }

    [Fact]
    public void Kill_Badge_flickering_for_a_single_check_yields_nothing()
    {
        Assert.Empty(Run(Blank(3), KillMarker(2), WithBadges(1, "kill_badge"), Blank(5)));
    }

    [Fact]
    public void Knockdown_Badge_after_a_Kill_Marker_yields_a_knockdown_and_no_kill()
    {
        var events = Run(Blank(3), KillMarker(2), WithBadges(20, "knockdown_badge"), Blank(5));

        var knockdown = Assert.Single(events);
        Assert.Equal(("knockdown", Start.AddMilliseconds(500)), (knockdown.Type, knockdown.OccurredAt));
    }

    [Fact]
    public void Knockdown_Badge_without_a_Kill_Marker_yields_nothing()
    {
        Assert.Empty(Run(Blank(3), WithBadges(20, "knockdown_badge"), Blank(5)));
    }

    [Fact]
    public void Knockdown_Badge_queued_before_the_Kill_Badge_of_the_same_Kill_Marker_is_no_knockdown()
    {
        // Under a Jammer the Score Popup showed the Knockdown Badge first and the Kill Badge later, after one Kill Marker.
        var events = Run(KillMarker(2), WithBadges(6, "knockdown_badge"), WithBadges(20, "knockdown_badge", "kill_badge"));

        Assert.Equal(["kill"], events.Select(e => e.Type));
    }

    [Fact]
    public void Knockdown_then_finishing_the_opponent_yields_knockdown_then_kill()
    {
        var events = Run(KillMarker(2), WithBadges(8, "knockdown_badge"), KillMarker(2), WithBadges(10, "knockdown_badge", "kill_badge"), Blank(20));

        Assert.Equal(["knockdown", "kill"], events.Select(e => e.Type));
        Assert.Equal([Start.AddMilliseconds(200), Start.AddMilliseconds(1200)], events.Select(e => e.OccurredAt));
        Assert.Equal([2, 3], events.Select(e => e.Sequence));
        Assert.Equal("1", events[1].Data["kills"]);
    }

    [Fact]
    public void Kill_Badge_without_a_Kill_Marker_yields_no_kill()
    {
        // The Squad finished an opponent the Player knocked down.
        Assert.Empty(Run(Blank(3), WithBadges(20, "kill_badge"), Blank(5)));
    }

    [Fact]
    public void Kill_Badge_long_after_a_Kill_Marker_yields_no_kill()
    {
        Assert.Empty(Run(KillMarker(2), Blank(20), WithBadges(20, "kill_badge"), Blank(5)));
    }

    [Fact]
    public void Kill_Badge_added_next_to_a_Knockdown_Badge_yields_one_kill()
    {
        var events = Run(KillMarker(2), WithBadges(8, "knockdown_badge"), KillMarker(2), WithBadges(10, "knockdown_badge", "kill_badge"));

        Assert.Equal(Start.AddMilliseconds(1200), Assert.Single(events, e => e.Type == "kill").OccurredAt);
    }

    [Fact]
    public void Second_Kill_Badge_added_to_the_Score_Popup_yields_a_second_kill()
    {
        var events = Run(KillMarker(2), WithBadges(8, "kill_badge"), KillMarker(2), WithBadges(10, "kill_badge", "kill_badge"));

        Assert.Equal([2, 3], events.Select(e => e.Sequence));
        Assert.Equal([Start.AddMilliseconds(200), Start.AddMilliseconds(1200)], events.Select(e => e.OccurredAt));
        Assert.Single(events.Select(e => e.MatchId).Distinct());
    }

    [Fact]
    public void Kill_Badge_in_a_new_Score_Popup_yields_a_new_kill()
    {
        var events = Run(KillMarker(2), WithBadges(10, "kill_badge"), Blank(20), KillMarker(2), WithBadges(10, "kill_badge"));

        Assert.Equal([Start.AddMilliseconds(200), Start.AddMilliseconds(3400)], events.Select(e => e.OccurredAt));
    }

    [Fact]
    public void Kill_Badge_missed_for_two_checks_is_not_counted_again()
    {
        var events = Run(KillMarker(2), WithBadges(10, "kill_badge"), Blank(2), WithBadges(10, "kill_badge"));

        Assert.Single(events);
    }

    [Fact]
    public void Kill_counter_flash_after_a_Kill_Marker_without_a_Kill_Badge_yields_one_kill()
    {
        // The Kill Badge was missed, for example against a very bright background.
        var events = Run(Blank(3), KillMarker(2), KillCounterFlash(6), Blank(20));

        Assert.Equal(Start.AddMilliseconds(500), Assert.Single(events).OccurredAt);
    }

    [Fact]
    public void Kill_counter_flash_without_a_Kill_Marker_yields_no_kill()
    {
        Assert.Empty(Run(Blank(3), KillCounterFlash(6), Blank(20)));
    }

    [Fact]
    public void Kill_counter_flash_with_a_Kill_Badge_yields_one_kill()
    {
        var events = Run(Blank(3), KillMarker(2), KillCounterFlash(3), WithBadges(20, "kill_badge"), Blank(5));

        Assert.Equal(Start.AddMilliseconds(800), Assert.Single(events).OccurredAt);
    }

    [Fact]
    public void Kill_counter_flash_after_a_Kill_Badge_yields_no_second_kill()
    {
        Assert.Single(Run(Blank(3), KillMarker(2), WithBadges(5, "kill_badge"), KillCounterFlash(6), Blank(20)));
    }

    [Fact]
    public void Kill_counter_flash_after_a_Kill_Badge_kill_does_not_use_an_older_knockdown_Kill_Marker()
    {
        // The knockdown's Kill Marker is still unused when the flash for the Kill Badge kill arrives.
        var events = Run(KillMarker(2), WithBadges(3, "knockdown_badge"), KillMarker(2), WithBadges(3, "knockdown_badge", "kill_badge"), KillCounterFlash(6), Blank(20));

        Assert.Equal(Start.AddMilliseconds(700), Assert.Single(events, e => e.Type == "kill").OccurredAt);
    }

    [Fact]
    public void Kill_at_1920x1080_yields_the_same_kill_as_at_2560x1440()
    {
        // The game renders the same HUD at a lower 16:9 resolution.
        static Mat[] At1080p(Mat[] frames) => frames.Select(f => f.Resize(new Size(1920, 1080), 0, 0, InterpolationFlags.Area)).ToArray();

        var events = Run(At1080p(Blank(3)), At1080p(KillMarker(2)), At1080p(WithBadges(20, "kill_badge")), At1080p(Blank(5)));

        Assert.Equal(Start.AddMilliseconds(500), Assert.Single(events).OccurredAt);
    }

    [Fact]
    public void Two_Kill_Markers_before_two_Kill_Badges_yield_two_kills()
    {
        var events = Run(KillMarker(2), Blank(2), KillMarker(2), WithBadges(5, "kill_badge"), WithBadges(10, "kill_badge", "kill_badge"));

        Assert.Equal([Start.AddMilliseconds(600), Start.AddMilliseconds(1100)], events.Select(e => e.OccurredAt));
    }

    [Fact]
    public void Kill_Marker_from_the_previous_Match_yields_no_kill()
    {
        var detector = new MatchDetector();
        var i = 0;
        IEnumerable<GameEvent> Feed(params Mat[][] segments) =>
            segments.SelectMany(s => s).SelectMany(image => detector.Process(new Frame(Start.AddMilliseconds(i++ * 100), image))).ToList();

        Feed(Blank(10), KillMarker(2));
        detector.GameExited(Start.AddSeconds(1.3));
        var events = Feed(Blank(5), WithBadges(10, "kill_badge"));

        Assert.Equal(["match_started"], events.Select(e => e.Type));
    }

    [Theory]
    [InlineData("#00FF00", 1)]
    [InlineData("#FF0000", 0)]
    public void Kill_Marker_in_another_colour_is_seen_only_when_that_colour_is_configured(string killMarkerColor, int kills)
    {
        // The Player set the Kill Marker to green in the game settings.
        var events = RunWith(new MatchDetector(killMarkerColor: killMarkerColor), Blank(3), KillMarker(2, hueShift: 60), WithBadges(20, "kill_badge"), Blank(5));

        Assert.Equal(kills, events.Count);
    }

    [Fact]
    public void Red_Kill_Marker_is_not_seen_when_green_is_configured()
    {
        Assert.Empty(RunWith(new MatchDetector(killMarkerColor: "#00FF00"), Blank(3), KillMarker(2), WithBadges(20, "kill_badge"), Blank(5)));
    }

    [Fact]
    public void White_Kill_Marker_cannot_be_told_from_the_hit_marker_so_a_Kill_Badge_alone_yields_a_kill()
    {
        var events = RunWith(new MatchDetector(killMarkerColor: "#FFFFFF"), Blank(3), WithBadges(20, "kill_badge"), Blank(5));

        Assert.Equal(Start.AddMilliseconds(300), Assert.Single(events).OccurredAt);
    }

    // Diagnostic mode: the Match Detector reports the cases where a Kill may have been missed.
    static List<(string What, DateTimeOffset At)> Unconfirmed(params Mat[][] segments)
    {
        var detector = new MatchDetector();
        var cases = new List<(string, DateTimeOffset)>();
        detector.Unconfirmed += (what, at) => cases.Add((what, at));
        RunWith(detector, segments);
        return cases;
    }

    [Fact]
    public void Kill_Badge_without_a_Kill_Marker_is_reported_as_unconfirmed()
    {
        Assert.Equal([("kill_badge_without_kill_marker", Start.AddMilliseconds(300))], Unconfirmed(Blank(3), WithBadges(20, "kill_badge"), Blank(5)));
    }

    [Fact]
    public void Kill_Marker_without_a_Kill_Badge_is_reported_as_unconfirmed_once_its_window_closes()
    {
        Assert.Equal([("kill_marker_without_kill_badge", Start.AddMilliseconds(300))], Unconfirmed(Blank(3), KillMarker(2), Blank(20)));
    }

    [Fact]
    public void Confirmed_kill_is_not_reported_as_unconfirmed()
    {
        Assert.Empty(Unconfirmed(Blank(3), KillMarker(2), WithBadges(20, "kill_badge"), Blank(20)));
    }

    static List<GameEvent> Run(params Mat[][] segments) => RunWith(new MatchDetector(), segments);

    static List<GameEvent> RunWith(MatchDetector detector, params Mat[][] segments)
    {
        foreach (var (image, n) in Blank(10).Select((f, n) => (f, n)))
            detector.Process(new Frame(Start.AddMilliseconds((n - 10) * 100), image));
        var events = new List<GameEvent>();
        var i = 0;
        foreach (var image in segments.SelectMany(s => s))
            events.AddRange(detector.Process(new Frame(Start.AddMilliseconds(i++ * 100), image)));
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

    // The red X of the Kill Marker around the crosshair; a hue shift (OpenCV units, 0-180) recolours it.
    static Mat[] KillMarker(int checks, int hueShift = 0)
    {
        var frame = BlankFrame();
        using var template = Cv2.ImRead(Path.Combine(Cues, "kill_marker.png"));
        if (hueShift != 0)
        {
            using var hsv = template.CvtColor(ColorConversionCodes.BGR2HSV);
            var channels = hsv.Split();
            Cv2.LUT(channels[0], Enumerable.Range(0, 256).Select(h => (byte)((h + hueShift) % 180)).ToArray(), channels[0]);
            Cv2.Merge(channels, hsv);
            Cv2.CvtColor(hsv, template, ColorConversionCodes.HSV2BGR);
        }
        template.CopyTo(frame[new Rect(1180, 620, template.Width, template.Height)]);
        return Enumerable.Repeat(frame, checks).ToArray();
    }

    // The kill counter in the counter bar while it flashes green.
    static Mat[] KillCounterFlash(int checks)
    {
        var frame = BlankFrame();
        using var template = Cv2.ImRead(Path.Combine(Cues, "kill_counter_flash.png"));
        template.CopyTo(frame[new Rect(2414, 82, template.Width, template.Height)]);
        return Enumerable.Repeat(frame, checks).ToArray();
    }

    // In the middle of a Match: the in-match HUD shows at the left of the counter bar.
    static Mat BlankFrame()
    {
        var frame = new Mat(1440, 2560, MatType.CV_8UC3, new Scalar(40, 40, 40));
        using var hud = Cv2.ImRead(Path.Combine(Cues, "in_match_hud.png"));
        hud.CopyTo(frame[new Rect(2190, 80, hud.Width, hud.Height)]);
        return frame;
    }
}
