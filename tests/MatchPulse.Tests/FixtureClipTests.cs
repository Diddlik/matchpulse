using System.Text.Json;
using OpenCvSharp;

namespace MatchPulse.Tests;

// Replays the clips in fixtures/clips and compares the Game Events with the clip's .expected.json.
public class FixtureClipTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(1));

    // Expected offsets were measured on the Kill Marker; the Kill Badge settles 0.4-0.6 s later.
    const double KillBadgeLagTolerance = 0.75;

    // Clips without the in-match HUD on screen need a Match that started earlier.
    static readonly string[] StartedMatchClips = ["kill_jammer_1119-2047"];

    public static TheoryData<string> AllClips() =>
        new(Directory.GetFiles(Clips, "*.expected.json").Select(f => Path.GetFileName(f)[..^".expected.json".Length]));

    [Theory]
    [MemberData(nameof(AllClips))]
    public void Clip_yields_its_expected_Game_Events(string clip)
    {
        var expected = ExpectedEvents(clip);

        var events = Replay(clip);

        Assert.Equal(expected.Select(e => e.Type), events.Select(e => e.Type));
        foreach (var ((type, offset, data), actual) in expected.Zip(events))
        {
            var tolerance = type == "kill" ? KillBadgeLagTolerance : 0.5;
            Assert.InRange((actual.OccurredAt - Start).TotalSeconds, offset - tolerance, offset + tolerance);
            Assert.Equal(data, actual.Data);
        }
        Assert.Equal(Enumerable.Range(events.FirstOrDefault()?.Sequence ?? 1, events.Count), events.Select(e => e.Sequence));
        Assert.True(events.Select(e => e.MatchId).Distinct().Count() <= 1);
    }

    [Fact]
    public void Full_Match_yields_match_started_its_kills_and_match_ended_under_one_matchId()
    {
        // A Match from the helicopter intro to the victory screen, replayed clip after clip by one Match Detector.
        var detector = new MatchDetector();
        var events = new List<GameEvent>();
        var at = Start;
        foreach (var clip in new[] { "match-start_1106-2253", "kill_direct_1108-2157", "kill_finish-downed_1105-2236", "victory_1108-2157" })
        {
            var last = at;
            foreach (var frame in ReplayFrameSource.Read(Path.Combine(Clips, clip + ".mkv"), at, checksPerSecond: 10))
                using (frame.Image)
                {
                    events.AddRange(detector.Process(frame));
                    last = frame.Timestamp;
                }
            at = last.AddSeconds(0.1);
        }

        Assert.Equal(["match_started", "kill", "kill", "match_ended"], events.Select(e => e.Type));
        Assert.Equal([1, 2, 3, 4], events.Select(e => e.Sequence));
        Assert.Single(events.Select(e => e.MatchId).Distinct());
        Assert.Equal("victory", events[^1].Data["result"]);
    }

    static readonly string[] KillClips =
    [
        "kill_direct_1108-2157",
        "kill_finish-downed_1105-2236",
        "kill_knock-then-finish_1105-2236",
        "kill_jammer_1119-2047",
        "knock_squad-finishes_1108-2157",
        "squad-kill-wipes-players-knock_1108-2157",
        "kill_then-wipe-credit_1105-2139",
    ];

    // Without the kill counter flash (no REDSEC counter bar), the Kill Badge alone must find every kill.
    public static TheoryData<string, bool> KillClipsWithAndWithoutCounterFlash()
    {
        var data = new TheoryData<string, bool>();
        foreach (var clip in KillClips)
        {
            data.Add(clip, true);
            data.Add(clip, false);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(KillClipsWithAndWithoutCounterFlash))]
    public void Clip_yields_its_expected_kills(string clip, bool killCounterFlash)
    {
        var expected = ExpectedKillOffsets(clip);

        var kills = Replay(clip, killCounterFlash: killCounterFlash).Where(e => e.Type == "kill").ToList();

        Assert.Equal(expected.Count, kills.Count);
        foreach (var (offset, kill) in expected.Zip(kills))
            Assert.InRange((kill.OccurredAt - Start).TotalSeconds, offset - KillBadgeLagTolerance, offset + KillBadgeLagTolerance);
    }

    [Theory]
    [InlineData("kill_knock-then-finish_1105-2236", 2.4)] // Kill Marker at 1.2 s, Knockdown Badge from 1.7 s
    [InlineData("kill_jammer_1119-2047", 2.2)] // Knockdown Badge from 1.5 s under a Jammer
    public void Knockdown_and_Kill_Marker_before_the_Kill_Badge_yield_no_kill(string clip, double until)
    {
        Assert.DoesNotContain(Replay(clip, until), e => e.Type == "kill");
    }

    static List<GameEvent> Replay(string clip, double until = double.MaxValue, bool killCounterFlash = true)
    {
        var detector = new MatchDetector(killCounterFlash: killCounterFlash);
        if (StartedMatchClips.Contains(clip)) StartMatch(detector);
        var events = new List<GameEvent>();
        foreach (var frame in ReplayFrameSource.Read(Path.Combine(Clips, clip + ".mkv"), Start, checksPerSecond: 10))
        {
            using (frame.Image)
            {
                if ((frame.Timestamp - Start).TotalSeconds >= until) break;
                events.AddRange(detector.Process(frame));
            }
        }
        return events;
    }

    // A Match started a minute before the clip, from frames showing the in-match HUD.
    static void StartMatch(MatchDetector detector)
    {
        using var hud = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "cues", "in_match_hud.png"));
        using var frame = new Mat(1440, 2560, MatType.CV_8UC3, new Scalar(40, 40, 40));
        hud.CopyTo(frame[new Rect(2190, 80, hud.Width, hud.Height)]);
        for (var i = 0; i < 10; i++)
            Assert.DoesNotContain(detector.Process(new Frame(Start.AddSeconds(-60 + 0.1 * i), frame)), e => e.Type != "match_started");
    }

    static List<(string Type, double Offset, Dictionary<string, string> Data)> ExpectedEvents(string clip)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Clips, clip + ".expected.json")));
        return json.RootElement.GetProperty("events").EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("offset").GetDouble(),
                e.GetProperty("data").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)))
            .ToList();
    }

    static List<double> ExpectedKillOffsets(string clip)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Clips, clip + ".expected.json")));
        return json.RootElement.GetProperty("events").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "kill")
            .Select(e => e.GetProperty("offset").GetDouble())
            .ToList();
    }

    static string Clips
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(dir.FullName, "fixtures"))) dir = dir.Parent!;
            return Path.Combine(dir.FullName, "fixtures", "clips");
        }
    }
}
