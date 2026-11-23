using MQTTnet;
using MQTTnet.Protocol;

namespace MatchPulse;

/// <summary>
/// MQTT plugin as an Event Sink: publishes each Game Event's JSON to <c>&lt;prefix&gt;/events/&lt;type&gt;</c> and keeps a
/// retained <c>&lt;prefix&gt;/status</c> of <c>online</c> / <c>offline</c> (the broker sets <c>offline</c> through the Last Will
/// if MatchPulse vanishes). Retained state topics follow the Match: <c>&lt;prefix&gt;/state/match</c> (ON/OFF), <c>kills</c>,
/// <c>knockdowns</c>, <c>deaths</c> and <c>last_event</c>. With the Home Assistant plugin it also publishes the discovery
/// config (<see cref="HomeAssistant"/>) and connects right away, so the device shows before the first Game Event.
/// The connection stays open and is made again when it was lost.
/// </summary>
public sealed class MqttSink : IEventSink, IAsyncDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    readonly MqttSettings settings;
    readonly HomeAssistantSettings? _homeAssistant;
    readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    readonly SemaphoreSlim _gate = new(1, 1);
    bool _match;
    int _kills, _knockdowns, _deaths;
    string _lastEvent = "none";

    /// <param name="homeAssistant">The Home Assistant plugin's settings; null leaves discovery topics alone.</param>
    /// <param name="later">The result of connecting at start (Home Assistant), reported like a delivery.</param>
    public MqttSink(MqttSettings settings, HomeAssistantSettings? homeAssistant = null, Action<IEventSink, Exception?>? later = null)
    {
        this.settings = settings;
        _homeAssistant = homeAssistant;
        if (homeAssistant is { Enabled: true })
            _ = Task.Run(async () =>
            {
                try
                {
                    await _gate.WaitAsync();
                    try { await ConnectAsync(CancellationToken.None); }
                    finally { _gate.Release(); }
                    later?.Invoke(this, null);
                }
                catch (ObjectDisposedException) { }
                catch (Exception e) { later?.Invoke(this, e); }
            });
    }

    public MqttSettings Settings => settings;

    string StatusTopic => $"{settings.TopicPrefix}/status";

    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await ConnectAsync(cancellationToken);
            await PublishAsync($"{settings.TopicPrefix}/events/{gameEvent.Type}", gameEvent.ToJson(), retain: false, cancellationToken);
            Follow(gameEvent);
            await PublishStateAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Connects with the settings and disconnects again; throws when the broker cannot be reached or refuses.</summary>
    public static async Task TestAsync(MqttSettings settings, CancellationToken cancellationToken, HomeAssistantSettings? homeAssistant = null)
    {
        await using var sink = new MqttSink(settings, homeAssistant is { Enabled: true } ? homeAssistant with { Enabled = false } : homeAssistant);
        await sink._gate.WaitAsync(cancellationToken);
        try { await sink.ConnectAsync(cancellationToken, homeAssistant); }
        finally { sink._gate.Release(); }
    }

    void Follow(GameEvent e)
    {
        switch (e.Type)
        {
            case "match_started": (_match, _kills, _knockdowns, _deaths) = (true, 0, 0, 0); break;
            case "match_ended": _match = false; break;
            case "kill": _kills = int.TryParse(e.Data.GetValueOrDefault("kills"), out var k) ? k : _kills + 1; break;
            case "knockdown": _knockdowns++; break;
            case "death": _deaths++; break;
        }
        _lastEvent = e.Type;
    }

    async Task PublishStateAsync(CancellationToken cancellationToken)
    {
        foreach (var (topic, value) in new[] { ("match", _match ? "ON" : "OFF"), ("kills", $"{_kills}"), ("knockdowns", $"{_knockdowns}"), ("deaths", $"{_deaths}"), ("last_event", _lastEvent) })
            await PublishAsync($"{settings.TopicPrefix}/state/{topic}", value, retain: true, cancellationToken);
    }

    async Task ConnectAsync(CancellationToken cancellationToken, HomeAssistantSettings? discovery = null)
    {
        if (_client.IsConnected) return;
        discovery ??= _homeAssistant;
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.Host, settings.Port)
            .WithClientId($"matchpulse-{Environment.MachineName}-{Environment.ProcessId}")
            .WithCleanSession()
            .WithTimeout(Timeout)
            .WithWillTopic(StatusTopic)
            .WithWillPayload("offline")
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
        if (settings.Username is not "") options.WithCredentials(settings.Username, settings.Password);
        if (settings.UseTls) options.WithTlsOptions(tls => tls.UseTls());

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var result = await _client.ConnectAsync(options.Build(), timeout.Token);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
            throw new MqttRefusedException($"MQTT broker refused the connection: {result.ResultCode}.");
        await PublishAsync(StatusTopic, "online", retain: true, cancellationToken);
        if (discovery is not null)
        {
            // Discovery on: (re)announce the device. Off: remove what an earlier run announced (an empty retained message).
            foreach (var (topic, config) in HomeAssistant.Discovery(settings, discovery, Updater.AppVersion.ToString()))
                await PublishAsync(topic, discovery.Enabled ? config.ToJsonString() : "", retain: true, cancellationToken);
            if (discovery.Enabled) await PublishStateAsync(cancellationToken);
        }
    }

    Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken) =>
        _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithContentType("application/json")
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)settings.Qos)
            .WithRetainFlag(retain)
            .Build(), cancellationToken);

    /// <summary>Says <c>offline</c> and disconnects cleanly (a clean disconnect does not send the Last Will).</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await PublishAsync(StatusTopic, "offline", retain: true, timeout.Token);
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), timeout.Token);
            }
        }
        catch (Exception) { }
        _client.Dispose();
        _gate.Dispose();
    }

    public override string ToString() => $"MQTT {settings.Host}:{settings.Port}";
}

public sealed class MqttRefusedException(string message) : Exception(message);
