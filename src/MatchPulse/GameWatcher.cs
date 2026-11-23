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
    /// <paramref name="gameLanguage"/> and <paramref name="killMarkerColor"/> are read each time <c>bf6.exe</c> starts, so a changed setting applies from the next game run without restarting MatchPulse.
    /// When stopped during a Match, <c>match_ended</c> with <c>reason: stopped</c> is dispatched before returning.
    /// With <paramref name="diagnosticsFolder"/>, unconfirmed Kill cases are saved there as snapshots (diagnostic mode).</summary>
    public static void Run(double checksPerSecond, Func<string> gameLanguage, Func<string> killMarkerColor, TimeSpan hudLostAfter, Action<GameEvent> dispatch, ILogger log, CancellationToken stop, Action<bool>? gameRunning = null, string? diagnosticsFolder = null)
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
            log.LogInformation("bf6.exe started on monitor {Monitor}: watching at {Checks} checks/s", monitor, checksPerSecond);
            gameRunning?.Invoke(true);
            var detector = new MatchDetector(log, hudLostAfter: hudLostAfter, gameLanguage: gameLanguage(), killMarkerColor: killMarkerColor());
            using var snapshots = diagnosticsFolder is null ? null : new DiagnosticSnapshots(diagnosticsFolder, log);
            if (snapshots is not null)
            {
                detector.Unconfirmed += snapshots.Write;
                log.LogInformation("Diagnostic mode: snapshots go to {Folder}", diagnosticsFolder);
            }
            while (!stop.IsCancellationRequested && IsGameRunning())
            {
                try
                {
                    var nextCheck = DateTimeOffset.MinValue;
                    foreach (var frame in LiveFrameSource.Read(monitor, checksPerSecond, stop, snapshots is null ? null : DiagnosticSnapshots.Regions))
                        using (frame.Image)
                        {
                            snapshots?.Add(frame);
                            foreach (var gameEvent in detector.Process(frame)) dispatch(gameEvent);
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
