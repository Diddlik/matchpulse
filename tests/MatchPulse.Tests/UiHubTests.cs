using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MatchPulse.Tests;

// The main window's host side: messages in, state and messages out. No WebView2 involved.
public sealed class UiHubTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 23, 20, 0, 0, TimeSpan.FromHours(1));
    readonly string _diagnostics = Path.Combine(Path.GetTempPath(), "mp-diag-" + Guid.NewGuid().ToString("N"));
    readonly List<(string Type, JsonNode? Payload)> _posted = [];
    Config _config = new([new Uri("http://localhost:8080/bf6")], Scripts: ["record.cmd"]);
    readonly UiHub _hub;

    public UiHubTests()
    {
        _hub = new UiHub(() => _config, c => { c.Save(Path.Combine(_diagnostics, "config.json")); _config = c; }, c => [], _diagnostics);
        _hub.Post += (type, payload) => { lock (_posted) _posted.Add((type, payload)); };
    }

    public void Dispose()
    {
        if (Directory.Exists(_diagnostics)) Directory.Delete(_diagnostics, true);
    }

    [Fact]
    public void Ready_is_answered_with_a_snapshot_of_settings_and_sinks()
    {
        _hub.Receive("""{"type":"ready","payload":null}""");

        var (type, snapshot) = Assert.Single(_posted);
        Assert.Equal("snapshot", type);
        Assert.Equal("off", (string?)snapshot!["appState"]);
        Assert.Equal(["webhook:http://localhost:8080/bf6", "script:record.cmd"], snapshot["sinks"]!.AsArray().Select(s => (string?)s!["id"]));
        Assert.Equal("#FF0000", (string?)snapshot["settings"]!["killMarkerColor"]);
        Assert.Equal(120, (double?)snapshot["settings"]!["matchEndWithoutHudSeconds"]);
    }

    [Fact]
    public void Match_started_and_kill_are_recorded_and_the_state_follows_the_Match()
    {
        _hub.SetGameRunning(true);
        _hub.SendAsync(new GameEvent("match_started", "m1", 1, Start, new Dictionary<string, string>()), default);
        _hub.SendAsync(new GameEvent("kill", "m1", 2, Start.AddMinutes(3), new Dictionary<string, string>()), default);

        var snapshot = _hub.Snapshot();
        Assert.Equal("match", (string?)snapshot["appState"]);
        Assert.Equal("m1", (string?)snapshot["match"]!["matchId"]);
        Assert.Equal(["match_started", "kill"], snapshot["events"]!.AsArray().Select(e => (string?)e!["type"]));
        Assert.Contains(_posted, p => p.Type == "gameEvent" && (string?)p.Payload!["type"] == "kill");

        _hub.SetGameRunning(false);
        Assert.Equal("off", (string?)_hub.Snapshot()["appState"]);
    }

    [Fact]
    public void Failed_delivery_shows_on_the_sink_and_raises_a_problem_once()
    {
        var problems = new List<string>();
        _hub.Problem += (key, _) => problems.Add(key);
        var webhook = new WebhookSink(new HttpClient(), new Uri("http://localhost:8080/bf6"));

        _hub.Delivered(webhook, new TaskCanceledException("timed out"));
        _hub.Delivered(webhook, new TaskCanceledException("timed out"));

        var sink = _hub.Snapshot()["sinks"]![0]!;
        Assert.Equal("timeout", (string?)sink["lastError"]!["code"]);
        Assert.NotNull(sink["lastFailureAt"]);
        Assert.Equal(["SinkFailed"], problems);

        _hub.Delivered(webhook, null);
        Assert.Null(_hub.Snapshot()["sinks"]![0]!["lastError"]);
    }

    [Fact]
    public void Saved_sinks_become_the_webhooks_and_scripts_of_the_config()
    {
        _hub.Receive("""{"type":"saveSinks","payload":[{"id":"x","kind":"script","target":" obs.cmd "},{"id":"y","kind":"webhook","target":"https://example.net/hook"}]}""");

        Assert.Equal([new Uri("https://example.net/hook")], _config.Webhooks);
        Assert.Equal(["obs.cmd"], _config.Scripts);
    }

    [Fact]
    public void Invalid_settings_are_not_applied_and_the_settings_in_force_are_sent_back()
    {
        _hub.Receive("""{"type":"saveSettings","payload":{"gameLanguage":"fr","killMarkerColor":"#00FF00"}}""");

        Assert.Equal("de", _config.GameLanguage);
        Assert.Contains(_posted, p => p.Type == "log" && (string?)p.Payload![0]!["level"] == "ERROR");
        Assert.Contains(_posted, p => p.Type == "settings" && (string?)p.Payload!["gameLanguage"] == "de");
    }

    [Fact]
    public void Saved_settings_map_to_the_config()
    {
        _hub.Receive("""{"type":"saveSettings","payload":{"gameLanguage":"en","uiLanguage":"de","theme":"dark","killMarkerColor":"#00FF00","checksPerSecond":7,"matchEndWithoutHudSeconds":90,"diagnosticMode":true,"notifyOnProblems":false,"liveMonitor":false}}""");

        Assert.Equal(("en", "de", "dark", "#00FF00", 7.0, 90.0, true, false),
            (_config.GameLanguage, _config.UiLanguage, _config.Theme, _config.KillMarkerColor, _config.ChecksPerSecond, _config.HudLostAfterSeconds, _config.Diagnostics, _config.NotifyOnProblems));
    }

    [Fact]
    public void Diagnostic_snapshots_are_listed_newest_first_and_cleared()
    {
        Directory.CreateDirectory(_diagnostics);
        File.WriteAllText(Path.Combine(_diagnostics, "2026-11-23_12-36-03.184_kill_badge_without_kill_marker.jpg"), "");
        File.WriteAllText(Path.Combine(_diagnostics, "2026-11-23_12-41-05.333_kill_marker_without_kill_badge.jpg"), "");

        var list = _hub.Diagnostics();

        Assert.Equal(["marker_without_skull", "skull_without_marker"], list.Select(d => (string?)d!["kind"]));
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 11, 23, 12, 41, 5, 333, DateTimeKind.Local)), (DateTimeOffset)list[0]!["capturedAt"]!);
        Assert.StartsWith("https://matchpulse.local/2026-11-23_12-41-05.333_", (string?)list[0]!["imageUrl"]);

        _hub.Receive("""{"type":"clearDiagnostics","payload":null}""");
        Assert.Empty(_hub.Diagnostics());
    }

    [Fact]
    public void Log_lines_are_classified_and_capture_failures_become_a_host_problem()
    {
        _hub.Logged(Start, LogLevel.Information, "Game Event {\"type\":\"kill\"}");
        _hub.Logged(Start, LogLevel.Warning, "Capture of monitor 1 failed; retrying");

        var snapshot = _hub.Snapshot();
        Assert.Equal(["EVENT", "WARN"], snapshot["log"]!.AsArray().Select(l => (string?)l!["level"]));
        Assert.Equal(["capture_failed"], snapshot["hostProblems"]!.AsArray().Select(p => (string?)p));
    }
}
