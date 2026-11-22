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

    /// <summary>Runs until <paramref name="stop"/> is cancelled. <paramref name="gameRunning"/> is told when <c>bf6.exe</c> starts and exits.</summary>
    public static void Run(double checksPerSecond, Action<GameEvent> dispatch, ILogger log, CancellationToken stop, Action<bool>? gameRunning = null)
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
            var detector = new MatchDetector(log);
            while (!stop.IsCancellationRequested && IsGameRunning())
            {
                try
                {
                    var nextCheck = DateTimeOffset.MinValue;
                    foreach (var frame in LiveFrameSource.Read(monitor, checksPerSecond, stop))
                        using (frame.Image)
                        {
                            foreach (var gameEvent in detector.Process(frame)) dispatch(gameEvent);
                            if (frame.Timestamp < nextCheck) continue;
                            if (!IsGameRunning()) break;
                            nextCheck = frame.Timestamp + TimeSpan.FromSeconds(1);
                        }
                }
                catch (Exception e)
                {
                    log.LogWarning(e, "Capture of monitor {Monitor} failed; retrying", monitor);
                    stop.WaitHandle.WaitOne(Poll);
                }
            }
            if (stop.IsCancellationRequested) return;

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
