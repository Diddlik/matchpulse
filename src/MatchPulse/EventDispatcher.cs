using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

public interface IEventSink
{
    Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Fans each Game Event out to all Event Sinks. Every sink has its own queue and worker, so a slow or
/// failing sink never blocks detection or the other sinks; failures are logged. Events reach each sink in order.
/// </summary>
public sealed class EventDispatcher
{
    readonly ILogger _log;
    readonly List<(Channel<GameEvent> Queue, Task Worker)> _sinks;

    public EventDispatcher(IEnumerable<IEventSink> sinks, ILogger log)
    {
        _log = log;
        _sinks = sinks.Select(sink =>
        {
            var queue = Channel.CreateUnbounded<GameEvent>(new UnboundedChannelOptions { SingleReader = true });
            return (queue, Task.Run(() => DeliverAsync(sink, queue.Reader)));
        }).ToList();
    }

    public void Dispatch(GameEvent gameEvent)
    {
        _log.LogInformation("Game Event {Json}", gameEvent.ToJson());
        foreach (var (queue, _) in _sinks) queue.Writer.TryWrite(gameEvent);
    }

    /// <summary>Stops accepting events and waits until every sink has handled its queue.</summary>
    public Task CompleteAsync()
    {
        foreach (var (queue, _) in _sinks) queue.Writer.Complete();
        return Task.WhenAll(_sinks.Select(s => s.Worker));
    }

    async Task DeliverAsync(IEventSink sink, ChannelReader<GameEvent> events)
    {
        await foreach (var gameEvent in events.ReadAllAsync())
        {
            try
            {
                await sink.SendAsync(gameEvent, CancellationToken.None);
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Event Sink {Sink} failed for {Type} #{Sequence}", sink, gameEvent.Type, gameEvent.Sequence);
            }
        }
    }
}
