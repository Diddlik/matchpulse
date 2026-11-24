using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace MatchPulse;

/// <summary>
/// Stream overlay as an Event Sink: a local web page for an OBS browser source at <c>http://localhost:&lt;port&gt;/</c>.
/// <c>/</c> serves <c>ui/overlay.html</c>; <c>/events</c> is a Server-Sent Events stream that starts with the current
/// state (<c>event: state</c>) and then carries every Game Event as its JSON.
/// </summary>
public sealed class OverlayServer : IEventSink, IDisposable
{
    readonly Lock _lock = new();
    readonly List<Stream> _clients = [];
    HttpListener? _listener;
    OverlaySettings? _settings;
    DateTimeOffset? _startedAt; // the Match, null outside one
    int _kills;

    /// <summary>After each (re)start: null when listening, else why not. Raised on the caller's thread.</summary>
    public event Action<Exception?>? Started;

    public static string Url(int port) => $"http://localhost:{port}/";

    /// <summary>Starts, stops or moves the server to match <paramref name="settings"/>.</summary>
    public void Configure(OverlaySettings settings)
    {
        lock (_lock)
        {
            if (settings == _settings) return;
            _settings = settings;
            Stop();
            if (!settings.Enabled) return;
        }
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(Url(settings.Port));
            listener.Start();
            lock (_lock) _listener = listener;
            _ = AcceptAsync(listener);
            Started?.Invoke(null);
        }
        catch (HttpListenerException e)
        {
            Started?.Invoke(new IOException($"The overlay cannot use port {settings.Port}: {e.Message}", e));
        }
    }

    public Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        string state;
        lock (_lock)
        {
            switch (gameEvent.Type)
            {
                case "match_started": _startedAt = gameEvent.OccurredAt; _kills = 0; break;
                case "match_ended": _startedAt = null; _kills = 0; break;
                case "kill" when int.TryParse(gameEvent.Data.GetValueOrDefault("kills"), CultureInfo.InvariantCulture, out var kills): _kills = kills; break;
            }
            state = State();
        }
        Broadcast($"data: {gameEvent.ToJson()}\n\nevent: state\ndata: {state}\n\n");
        return Task.CompletedTask;
    }

    string State() => new JsonObject { ["startedAt"] = _startedAt, ["kills"] = _kills }.ToJsonString();

    async Task AcceptAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = HandleAsync(context);
        }
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            switch (context.Request.Url?.AbsolutePath)
            {
                case "/":
                    var page = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "ui", "overlay.html"));
                    response.ContentType = "text/html; charset=utf-8";
                    await response.OutputStream.WriteAsync(page);
                    response.Close();
                    break;
                case "/events":
                    response.ContentType = "text/event-stream";
                    response.Headers["Cache-Control"] = "no-cache";
                    response.SendChunked = true;
                    string state;
                    lock (_lock) state = State();
                    await Write(response.OutputStream, $"retry: 2000\nevent: state\ndata: {state}\n\n");
                    lock (_lock) _clients.Add(response.OutputStream); // stays open; closed by Stop or a failed write
                    break;
                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
        catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException)
        {
            response.Abort();
        }
    }

    void Broadcast(string message)
    {
        Stream[] clients;
        lock (_lock) clients = [.. _clients];
        foreach (var client in clients)
            _ = Task.Run(async () =>
            {
                try { await Write(client, message); }
                catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException)
                {
                    lock (_lock) _clients.Remove(client);
                }
            });
    }

    static async Task Write(Stream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        await stream.FlushAsync();
    }

    // Under _lock.
    void Stop()
    {
        foreach (var client in _clients) try { client.Dispose(); } catch (Exception e) when (e is HttpListenerException or IOException) { }
        _clients.Clear();
        _listener?.Close();
        _listener = null;
    }

    public void Dispose()
    {
        lock (_lock) Stop();
    }

    public override string ToString() => "Overlay";
}
