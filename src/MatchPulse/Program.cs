using MatchPulse;
using Microsoft.Extensions.Logging;

// Usage: MatchPulse replay <video.mkv|mp4> [--config <config.json>]
if (args is not ["replay", var video, ..])
{
    Console.Error.WriteLine("Usage: MatchPulse replay <video> [--config <path>]");
    return 2;
}
var configPath = args is [_, _, "--config", var path] ? path : Config.DefaultPath;

using var logging = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff "));
var log = logging.CreateLogger("MatchPulse");

var config = Config.Load(configPath);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var dispatcher = new EventDispatcher(
    config.Webhooks.Select(url => (IEventSink)new WebhookSink(http, url))
        .Concat((config.Scripts ?? []).Select(command => new ScriptSink(command, TimeSpan.FromSeconds(10)))),
    log);
var detector = new MatchDetector(log);

log.LogInformation("Replaying {Video} at {Checks} checks/s to {Count} webhook(s) and {Scripts} script(s)", video, config.ChecksPerSecond, config.Webhooks.Count, config.Scripts?.Count ?? 0);
foreach (var frame in ReplayFrameSource.Read(video, DateTimeOffset.Now, config.ChecksPerSecond))
    using (frame.Image)
        foreach (var gameEvent in detector.Process(frame))
            dispatcher.Dispatch(gameEvent);

await dispatcher.CompleteAsync();
return 0;
