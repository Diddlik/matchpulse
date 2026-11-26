using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

/// <summary>
/// Game Watcher: waits for <c>bf6.exe</c>, captures the monitor its window is on while it runs, and ends a
/// running Match when it exits. It only enumerates processes and looks up the window's monitor; it never
/// opens, reads or injects into the game process.
/// </summary>
public static class GameWatcher
{
    static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    // Match Name: the lobby header is read this often while no Match runs; reads older than NameFreshFor at match_started
    // belong to an earlier lobby (the REDSEC lobby and helicopter intro take a few minutes).
    static readonly TimeSpan NameReadEvery = TimeSpan.FromSeconds(2), NameFreshFor = TimeSpan.FromMinutes(5);

    /// <summary>Runs until <paramref name="stop"/> is cancelled. <paramref name="gameRunning"/> is told when <c>bf6.exe</c> starts and exits.
    /// <paramref name="config"/> is read each time <c>bf6.exe</c> starts, so changed settings apply from the next game run without restarting MatchPulse.
    /// When stopped during a Match, <c>match_ended</c> with <c>reason: stopped</c> is dispatched before returning.
    /// With <see cref="Config.Diagnostics"/>, unconfirmed Kill cases are saved to <see cref="DiagnosticSnapshots.DefaultFolder"/> (diagnostic mode).</summary>
    /// With <see cref="Config.LiveMonitor"/> (read at each kill), a full-screen picture of each kill is saved to
    /// <see cref="KillPictures.DefaultFolder"/> and reported to <paramref name="killPicture"/>.
    /// <c>match_started</c> carries the Match Name read in the lobby as <c>data.mode</c> and <c>data.map</c>; names not in
    /// <see cref="Config.MatchNames"/> yet are reported to <paramref name="newMatchNames"/>.
    public static void Run(Func<Config> config, Action<GameEvent> dispatch, ILogger log, CancellationToken stop, Action<bool>? gameRunning = null, Action<GameEvent, string>? killPicture = null,
        Action<IReadOnlyList<string>>? newMatchNames = null)
    {
        log.LogInformation("Waiting for bf6.exe");
        while (!stop.IsCancellationRequested)
        {
            var window = FindGameWindow();
            if (window == 0)
            {
                stop.WaitHandle.WaitOne(Poll);
                continue;
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var settings = config();
            var checksPerSecond = settings.ChecksPerSecond;
            var diagnosticsFolder = settings.Diagnostics ? DiagnosticSnapshots.DefaultFolder : null;
            log.LogInformation("bf6.exe started on monitor {Monitor}: watching at {Checks} checks/s", monitor, checksPerSecond);
            gameRunning?.Invoke(true);
            var detector = new MatchDetector(log, hudLostAfter: TimeSpan.FromSeconds(settings.HudLostAfterSeconds), gameLanguage: settings.GameLanguage, killMarkerColor: settings.KillMarkerColor);
            using var snapshots = diagnosticsFolder is null ? null : new DiagnosticSnapshots(diagnosticsFolder, log);
            if (snapshots is not null)
            {
                detector.Unconfirmed += snapshots.Write;
                log.LogInformation("Diagnostic mode: snapshots go to {Folder}", diagnosticsFolder);
            }
            var pictures = new KillPictures(KillPictures.DefaultFolder, log);
            var names = MatchNames.Create(settings.GameLanguage);
            if (names is null) log.LogWarning("Windows has no OCR for game language {Language}: Match Names are not read", settings.GameLanguage);
            var nameReads = new List<(DateTimeOffset At, string Mode, string Map)>();
            var nextNameRead = DateTimeOffset.MinValue;
            var inMatch = false;
            var paused = false;
            while (!stop.IsCancellationRequested && IsGameRunning())
            {
                // Paused (Config.Active off): no capture and no Game Events; a running Match ends with reason "stopped".
                if (!config().Active)
                {
                    if (!paused)
                    {
                        paused = true;
                        log.LogInformation("MatchPulse paused: capture stopped");
                        foreach (var gameEvent in detector.Stopped(DateTimeOffset.Now)) dispatch(gameEvent);
                        inMatch = false;
                    }
                    stop.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
                    continue;
                }
                if (paused)
                {
                    paused = false;
                    log.LogInformation("MatchPulse active again: capture resumed");
                }
                try
                {
                    var nextCheck = DateTimeOffset.MinValue;
                    foreach (var frame in LiveFrameSource.Read(monitor, checksPerSecond, stop, [MatchNames.Region, .. snapshots is null ? [] : DiagnosticSnapshots.Regions], () => pictures.Wanted))
                        using (frame.Image)
                        {
                            if (!config().Active) break;
                            if (pictures.Save(frame) is { } saved) killPicture?.Invoke(saved.Kill, saved.Path);
                            snapshots?.Add(frame);
                            if (!inMatch && names is not null && frame.Timestamp >= nextNameRead)
                            {
                                nextNameRead = frame.Timestamp + NameReadEvery;
                                if (names.Read(frame.Image) is { } read) nameReads.Add((frame.Timestamp, read.Mode, read.Map));
                            }
                            foreach (var detected in detector.Process(frame))
                            {
                                var gameEvent = detected;
                                if (gameEvent.Type == "match_started")
                                {
                                    inMatch = true;
                                    gameEvent = Named(gameEvent, nameReads, config().MatchNames, newMatchNames, log);
                                    nameReads.Clear();
                                }
                                else if (gameEvent.Type == "match_ended") inMatch = false;
                                dispatch(gameEvent);
                                if (gameEvent.Type == "kill" && config().LiveMonitor) pictures.Request(gameEvent);
                            }
                            if (frame.Timestamp < nextCheck) continue;
                            if (!IsGameRunning()) break;
                            if (FindGameWindow() is var moved and not 0 && MonitorFromWindow(moved, MonitorDefaultToNearest) is var now && now != monitor)
                            {
                                log.LogInformation("bf6.exe moved to monitor {Monitor}: restarting capture", now);
                                monitor = now;
                                break;
                            }
                            nextCheck = frame.Timestamp + TimeSpan.FromSeconds(1);
                        }
                }
                catch (Exception e)
                {
                    log.LogWarning(e, "Capture of monitor {Monitor} failed; retrying", monitor);
                    stop.WaitHandle.WaitOne(Poll);
                }
            }
            if (stop.IsCancellationRequested)
            {
                foreach (var gameEvent in detector.Stopped(DateTimeOffset.Now)) dispatch(gameEvent);
                return;
            }

            log.LogInformation("bf6.exe exited: watching stopped");
            foreach (var gameEvent in detector.GameExited(DateTimeOffset.Now)) dispatch(gameEvent);
            gameRunning?.Invoke(false);
        }
    }

    /// <summary><paramref name="e"/> with the Match Name agreed on by the recent lobby reads, each replaced by the known name nearly the same.</summary>
    static GameEvent Named(GameEvent e, List<(DateTimeOffset At, string Mode, string Map)> reads, IReadOnlyList<string> known, Action<IReadOnlyList<string>>? newNames, ILogger log)
    {
        var recent = reads.Where(r => e.OccurredAt - r.At <= NameFreshFor).ToList();
        if (recent.Count == 0)
        {
            log.LogInformation("No Match Name read in the lobby");
            return e;
        }
        var mode = MatchNames.Resolve(MatchNames.Consensus([.. recent.Select(r => r.Mode)]), known);
        var map = MatchNames.Resolve(MatchNames.Consensus([.. recent.Select(r => r.Map)]), known);
        log.LogInformation("Match Name {Mode}, {Map} from {Reads} lobby read(s)", mode.Name, map.Name, recent.Count);
        if (new[] { mode, map }.Where(n => !n.Known).Select(n => n.Name).Distinct().ToList() is { Count: > 0 } added) newNames?.Invoke(added);
        return e with { Data = new Dictionary<string, string>(e.Data) { ["mode"] = mode.Name, ["map"] = map.Name } };
    }

    // Process enumeration takes a system snapshot; no handle to the game process is opened.
    static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("bf6");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    // MainWindowHandle enumerates top-level windows by process id; 0 while the game has no window yet.
    static IntPtr FindGameWindow()
    {
        var processes = Process.GetProcessesByName("bf6");
        try
        {
            return processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != 0);
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
}
