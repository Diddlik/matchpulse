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

    /// <summary>Runs until <paramref name="stop"/> is cancelled. <paramref name="gameRunning"/> is told when <c>bf6.exe</c> starts and exits.
    /// <paramref name="config"/> is read each time <c>bf6.exe</c> starts, so changed settings apply from the next game run without restarting MatchPulse.
    /// When stopped during a Match, <c>match_ended</c> with <c>reason: stopped</c> is dispatched before returning.
    /// With <see cref="Config.Diagnostics"/>, unconfirmed Kill cases are saved to <see cref="DiagnosticSnapshots.DefaultFolder"/> (diagnostic mode).</summary>
    /// With <see cref="Config.LiveMonitor"/> (read at each kill), a full-screen picture of each kill is saved to
    /// <see cref="KillPictures.DefaultFolder"/> and reported to <paramref name="killPicture"/>.
    public static void Run(Func<Config> config, Action<GameEvent> dispatch, ILogger log, CancellationToken stop, Action<bool>? gameRunning = null, Action<GameEvent, string>? killPicture = null)
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
                    foreach (var frame in LiveFrameSource.Read(monitor, checksPerSecond, stop, snapshots is null ? null : DiagnosticSnapshots.Regions, () => pictures.Wanted))
                        using (frame.Image)
                        {
                            if (!config().Active) break;
                            if (pictures.Save(frame) is { } saved) killPicture?.Invoke(saved.Kill, saved.Path);
                            snapshots?.Add(frame);
                            foreach (var gameEvent in detector.Process(frame))
                            {
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
