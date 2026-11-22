using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

static class Program
{
    /// <summary>
    /// No arguments: tray app. <c>MatchPulse replay &lt;video&gt; [--config &lt;path&gt;]</c>: replay a recording to the configured webhooks.
    /// Both log to the console and to <see cref="FileLog.DefaultPath"/>.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        AttachConsole(-1); // WinExe: write to the parent terminal's console, if there is one
        using var file = new FileLog(FileLog.DefaultPath);
        using var logging = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ").AddProvider(file));
        var log = logging.CreateLogger("MatchPulse");

        switch (args)
        {
            case []:
                ApplicationConfiguration.Initialize();
                Application.Run(new TrayApp(Config.DefaultPath, file.FilePath, log));
                return 0;
            case ["replay", var video, ..]:
                return Replay(video, args is [_, _, "--config", var path] ? path : Config.DefaultPath, log);
            default:
                Console.Error.WriteLine("Usage: MatchPulse [replay <video> [--config <path>]]");
                return 2;
        }
    }

    static int Replay(string video, string configPath, ILogger log)
    {
        var config = Config.Load(configPath);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var dispatcher = new EventDispatcher(config.Webhooks.Select(url => new WebhookSink(http, url)), log);
        var detector = new MatchDetector(log);

        log.LogInformation("Replaying {Video} at {Checks} checks/s to {Count} webhook(s)", video, config.ChecksPerSecond, config.Webhooks.Count);
        foreach (var frame in ReplayFrameSource.Read(video, DateTimeOffset.Now, config.ChecksPerSecond))
            using (frame.Image)
                foreach (var gameEvent in detector.Process(frame))
                    dispatcher.Dispatch(gameEvent);

        dispatcher.CompleteAsync().GetAwaiter().GetResult();
        return 0;
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
