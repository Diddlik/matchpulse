using System.Text.Json;
using OpenCvSharp;

namespace MatchPulse.Tests;

// Replays the clips in fixtures/clips and compares the kill events with the clip's .expected.json.
// A Match is treated as already started (match_started is ticket 04), so only kill events are compared.
public class FixtureClipTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(1));

    // Expected offsets were measured on the Kill Marker; the Kill Badge settles 0.4-0.6 s later.
    const double KillBadgeLagTolerance = 0.75;

    static readonly string[] KillClips =
    [
        "kill_direct_1108-2157",
        "kill_finish-downed_1105-2236",
        "kill_knock-then-finish_1105-2236",
        "kill_jammer_1119-2047",
        "knock_squad-finishes_1108-2157",
        "squad-kill-wipes-players-knock_1108-2157",
        "kill_then-wipe-credit_1105-2139",
        "en_squad-credit_redsec-en2",
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

    // The game at 1920x1080 draws the same HUD at 0.75 of the 2560x1440 size; downscaled clips stand in for it.
    [Theory]
    [MemberData(nameof(KillClipsAt1440p))]
    public void Clip_at_1920x1080_yields_the_same_kills(string clip)
    {
        var at1440p = Replay(clip).Where(e => e.Type == "kill").Select(e => e.OccurredAt);

        var at1080p = Replay(clip, resizeTo: new Size(1920, 1080)).Where(e => e.Type == "kill").Select(e => e.OccurredAt);

        Assert.Equal(at1440p, at1080p);
    }

    public static TheoryData<string> KillClipsAt1440p() => new(KillClips.Where(c => !c.StartsWith("en_")));

    [Theory]
    [InlineData("kill_knock-then-finish_1105-2236", 2.4)] // Kill Marker at 1.2 s, Knockdown Badge from 1.7 s
    [InlineData("kill_jammer_1119-2047", 2.2)] // Knockdown Badge from 1.5 s under a Jammer
    public void Knockdown_and_Kill_Marker_before_the_Kill_Badge_yield_no_kill(string clip, double until)
    {
        Assert.Empty(Replay(clip, until));
    }

    // The Match Detector uses the clip's game language from its .expected.json.
    static List<GameEvent> Replay(string clip, double until = double.MaxValue, bool killCounterFlash = true, Size? resizeTo = null)
    {
        var detector = new MatchDetector(killCounterFlash: killCounterFlash, gameLanguage: Expected(clip).GetProperty("gameLanguage").GetString()!);
        var events = new List<GameEvent>();
        foreach (var frame in ReplayFrameSource.Read(Path.Combine(Clips, clip + ".mkv"), Start, checksPerSecond: 10))
        {
            using (frame.Image)
            {
                if ((frame.Timestamp - Start).TotalSeconds >= until) break;
                if (resizeTo is { } size)
                {
                    using var resized = frame.Image.Resize(size, 0, 0, InterpolationFlags.Area);
                    events.AddRange(detector.Process(frame with { Image = resized }));
                }
                else events.AddRange(detector.Process(frame));
            }
        }
        return events;
    }

    static JsonElement Expected(string clip) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Clips, clip + ".expected.json"))).RootElement;

    static List<double> ExpectedKillOffsets(string clip)
    {
        return Expected(clip).GetProperty("events").EnumerateArray()
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
