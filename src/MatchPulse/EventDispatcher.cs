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
    readonly Lock _lock = new();
    List<(Channel<GameEvent> Queue, Task Worker)> _sinks;
    readonly List<Task> _replaced = []; // workers of replaced sinks, still delivering their queues

    /// <summary>After each delivery attempt: the sink and the failure, or null on success. Raised on the sink's worker.</summary>
    public event Action<IEventSink, Exception?>? Delivered;

    public EventDispatcher(IEnumerable<IEventSink> sinks, ILogger log)
    {
        _log = log;
        _sinks = Start(sinks);
    }

    public void Dispatch(GameEvent gameEvent)
    {
        _log.LogInformation("Game Event {Json}", gameEvent.ToJson());
        lock (_lock)
            foreach (var (queue, _) in _sinks)
                if (!queue.Writer.TryWrite(gameEvent))
                    _log.LogWarning("Game Event {Type} #{Sequence} dropped: the Event Dispatcher is complete", gameEvent.Type, gameEvent.Sequence);
    }

    /// <summary>Sends later Game Events to <paramref name="sinks"/>; events already queued still reach the old sinks.</summary>
    public void ReplaceSinks(IEnumerable<IEventSink> sinks)
    {
        var started = Start(sinks);
        lock (_lock)
        {
            foreach (var (queue, worker) in _sinks)
            {
                queue.Writer.TryComplete();
                _replaced.Add(worker);
            }
            _sinks = started;
        }
    }

    /// <summary>Stops accepting events and waits until every sink has handled its queue.</summary>
    public Task CompleteAsync()
    {
        lock (_lock)
        {
            foreach (var (queue, _) in _sinks) queue.Writer.TryComplete();
            return Task.WhenAll([.. _replaced, .. _sinks.Select(s => s.Worker)]);
        }
    }

    List<(Channel<GameEvent> Queue, Task Worker)> Start(IEnumerable<IEventSink> sinks) =>
        sinks.Select(sink =>
        {
            var queue = Channel.CreateUnbounded<GameEvent>(new UnboundedChannelOptions { SingleReader = true });
            return (queue, Task.Run(() => DeliverAsync(sink, queue.Reader)));
        }).ToList();

    async Task DeliverAsync(IEventSink sink, ChannelReader<GameEvent> events)
    {
        await foreach (var gameEvent in events.ReadAllAsync())
        {
            try
            {
                await sink.SendAsync(gameEvent, CancellationToken.None);
                Delivered?.Invoke(sink, null);
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Event Sink {Sink} failed for {Type} #{Sequence}", sink, gameEvent.Type, gameEvent.Sequence);
                Delivered?.Invoke(sink, e);
            }
        }
    }
}
