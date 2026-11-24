using System.Net;
using System.Net.Sockets;

namespace MatchPulse.Tests;

public sealed class OverlayAndReelTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 24, 20, 0, 0, TimeSpan.FromHours(1));

    static HighlightReel.Replay At(string path, double seconds) => new(path, Start.AddSeconds(seconds));

    [Fact]
    public void Replays_far_apart_give_one_clip_each()
    {
        var cuts = HighlightReel.Cuts([At("b", 60), At("a", 0)], 10);
        Assert.Equal([("a", 10.0), ("b", 10.0)], cuts);
    }

    [Fact]
    public void Overlapping_replays_use_the_later_one_cut_to_cover_the_run()
    {
        // 0, 4 and 9 s overlap with 10 s clips; 30 s starts a new run.
        var cuts = HighlightReel.Cuts([At("a", 0), At("b", 4), At("c", 9), At("d", 30)], 10);
        Assert.Equal([("c", 19.0), ("d", 10.0)], cuts);
    }

    [Fact]
    public async Task Overlay_serves_the_page_and_streams_state_and_events()
    {
        var port = FreePort();
        using var server = new OverlayServer();
        Exception? started = new();
        server.Started += e => started = e;
        server.Configure(new OverlaySettings(true, port));
        Assert.Null(started);

        using var http = new HttpClient { BaseAddress = new Uri(OverlayServer.Url(port)) };
        Assert.Contains("EventSource", await http.GetStringAsync("/"));

        await server.SendAsync(new GameEvent("match_started", "m1", 1, Start, new Dictionary<string, string>()), default);
        using var stream = await http.GetStreamAsync("/events");
        using var reader = new StreamReader(stream);
        Assert.Equal("retry: 2000", await reader.ReadLineAsync());
        Assert.Equal("event: state", await reader.ReadLineAsync());
        Assert.Contains("\"kills\":0", await reader.ReadLineAsync());

        await server.SendAsync(new GameEvent("kill", "m1", 2, Start, new Dictionary<string, string> { ["kills"] = "1" }), default);
        string? line;
        do line = await reader.ReadLineAsync(); while (line is not null && !line.StartsWith("data: {\"type\""));
        Assert.Contains("\"type\":\"kill\"", line);
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
