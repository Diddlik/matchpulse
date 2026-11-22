namespace MatchPulse;

public enum TrayState { GameNotRunning, GameRunning, MatchRunning }

/// <summary>
/// What the tray icon shows. The Game Watcher reports the game via <see cref="SetGameRunning"/>; as an Event Sink
/// it follows <c>match_started</c> and <c>match_ended</c>. <see cref="Changed"/> fires on any thread.
/// </summary>
public sealed class AppStatus : IEventSink
{
    volatile bool _game, _match;

    public event Action<TrayState>? Changed;

    public TrayState State => _match ? TrayState.MatchRunning : _game ? TrayState.GameRunning : TrayState.GameNotRunning;

    public void SetGameRunning(bool running)
    {
        _game = running;
        if (!running) _match = false;
        Changed?.Invoke(State);
    }

    public Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        if (gameEvent.Type is "match_started" or "match_ended")
        {
            _match = gameEvent.Type == "match_started";
            Changed?.Invoke(State);
        }
        return Task.CompletedTask;
    }

    public override string ToString() => "tray status";
}
