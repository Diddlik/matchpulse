using MatchPulse;
using Microsoft.Extensions.Logging;

// Usage: MatchPulse replay <video.mkv|mp4> [--config <config.json>]
//        MatchPulse live [--config <config.json>]
string? video;
switch (args)
{
    case ["replay", var v, .. var rest] when rest is [] or ["--config", _]: video = v; break;
    case ["live"] or ["live", "--config", _]: video = null; break;
    default:
        Console.Error.WriteLine("Usage: MatchPulse replay <video> [--config <path>]\n       MatchPulse live [--config <path>]");
        return 2;
}
var configPath = args is [.., "--config", var path] ? path : Config.DefaultPath;

using var logging = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff "));
var log = logging.CreateLogger("MatchPulse");

var config = Config.Load(configPath);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var dispatcher = new EventDispatcher(
    config.Webhooks.Select(url => (IEventSink)new WebhookSink(http, url))
        .Concat((config.Scripts ?? []).Select(command => new ScriptSink(command, TimeSpan.FromSeconds(10)))),
    log);

if (video is null)
{
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    GameWatcher.Run(config.ChecksPerSecond, dispatcher.Dispatch, log, stop.Token);
}
else
{
    var detector = new MatchDetector(log);
    log.LogInformation("Replaying {Video} at {Checks} checks/s to {Count} webhook(s) and {Scripts} script(s)", video, config.ChecksPerSecond, config.Webhooks.Count, config.Scripts?.Count ?? 0);
    foreach (var frame in ReplayFrameSource.Read(video, DateTimeOffset.Now, config.ChecksPerSecond))
        using (frame.Image)
            foreach (var gameEvent in detector.Process(frame))
                dispatcher.Dispatch(gameEvent);
}

await dispatcher.CompleteAsync();
return 0;
