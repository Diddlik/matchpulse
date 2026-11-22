using Microsoft.Extensions.Logging.Abstractions;

namespace MatchPulse.Tests;

public class EventDispatcherTests
{
    sealed class Recorder : IEventSink
    {
        public readonly List<int> Received = [];

        public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
        {
            await Task.Delay(20, cancellationToken); // slow enough that events are still queued when the sinks are replaced
            lock (Received) Received.Add(gameEvent.Sequence);
        }
    }

    [Fact]
    public async Task Replacing_the_Event_Sinks_drops_no_Game_Event()
    {
        var before = new Recorder();
        var after = new Recorder();
        var dispatcher = new EventDispatcher([before], NullLogger.Instance);

        for (var i = 1; i <= 5; i++) dispatcher.Dispatch(Kill(i));
        dispatcher.ReplaceSinks([after]);
        for (var i = 6; i <= 8; i++) dispatcher.Dispatch(Kill(i));
        await dispatcher.CompleteAsync();

        Assert.Equal([1, 2, 3, 4, 5], before.Received);
        Assert.Equal([6, 7, 8], after.Received);
    }

    static GameEvent Kill(int sequence) => new("kill", "m", sequence, DateTimeOffset.Now, new Dictionary<string, string>());
}
