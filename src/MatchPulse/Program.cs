using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

static class Program
{
    /// <summary>
    /// No arguments: tray app. <c>MatchPulse replay &lt;video&gt; [--config &lt;path&gt;]</c>: replay a recording to the configured Event Sinks.
    /// <c>MatchPulse live [--config &lt;path&gt;]</c>: run the Game Watcher in the terminal until Ctrl+C. All modes log to the console and to <see cref="FileLog.DefaultPath"/>.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        AttachConsole(-1); // WinExe: write to the parent terminal's console, if there is one
        using var file = new FileLog(FileLog.DefaultPath);
        using var logging = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ").AddProvider(file));
        var log = logging.CreateLogger("MatchPulse");

        var configPath = args is [.., "--config", var path] ? path : Config.DefaultPath;
        switch (args)
        {
            case [] or ["--show"]:
                ApplicationConfiguration.Initialize();
                Application.Run(new TrayApp(Config.DefaultPath, file, log, showWindow: args is ["--show"]));
                return 0;
            case ["replay", var video, .. var rest] when rest is [] or ["--config", _]:
                return Replay(video, configPath, log);
            case ["live"] or ["live", "--config", _]:
                return Live(configPath, log);
            default:
                Console.Error.WriteLine("Usage: MatchPulse [--show]\n       MatchPulse replay <video> [--config <path>]\n       MatchPulse live [--config <path>]");
                return 2;
        }
    }

    static int Live(string configPath, ILogger log)
    {
        var config = configPath == Config.DefaultPath ? Config.LoadOrDefault(configPath) : Config.Load(configPath);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var dispatcher = new EventDispatcher(Sinks(config, http), log);
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        GameWatcher.Run(() => config, dispatcher.Dispatch, log, stop.Token);
        dispatcher.CompleteAsync().GetAwaiter().GetResult();
        return 0;
    }

    static int Replay(string video, string configPath, ILogger log)
    {
        var config = configPath == Config.DefaultPath ? Config.LoadOrDefault(configPath) : Config.Load(configPath);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var dispatcher = new EventDispatcher(Sinks(config, http), log);
        var detector = new MatchDetector(log, hudLostAfter: TimeSpan.FromSeconds(config.HudLostAfterSeconds), gameLanguage: config.GameLanguage, killMarkerColor: config.KillMarkerColor);

        using var snapshots = config.Diagnostics ? new DiagnosticSnapshots(DiagnosticSnapshots.DefaultFolder, log) : null;
        if (snapshots is not null) detector.Unconfirmed += snapshots.Write;
        log.LogInformation("Replaying {Video} at {Checks} checks/s to {Count} webhook(s) and {Scripts} script(s)", video, config.ChecksPerSecond, config.Webhooks.Count, config.Scripts.Count);
        foreach (var frame in ReplayFrameSource.Read(video, DateTimeOffset.Now, config.ChecksPerSecond))
            using (frame.Image)
            {
                snapshots?.Add(frame);
                foreach (var gameEvent in detector.Process(frame))
                    dispatcher.Dispatch(gameEvent);
            }

        dispatcher.CompleteAsync().GetAwaiter().GetResult();
        return 0;
    }

    /// <summary>The Event Sinks the config file asks for.</summary>
    public static IEnumerable<IEventSink> Sinks(Config config, HttpClient http) =>
        [
            .. config.Webhooks.Select(url => new WebhookSink(http, url)),
            .. config.Scripts.Select(command => new ScriptSink(command, TimeSpan.FromSeconds(10))),
        ];

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
