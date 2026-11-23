using MQTTnet;
using MQTTnet.Protocol;

namespace MatchPulse;

/// <summary>
/// MQTT plugin as an Event Sink: publishes each Game Event's JSON to <c>&lt;prefix&gt;/events/&lt;type&gt;</c> and keeps a
/// retained <c>&lt;prefix&gt;/status</c> of <c>online</c> / <c>offline</c> (the broker sets <c>offline</c> through the Last Will
/// if MatchPulse vanishes). The connection stays open and is made again when it was lost.
/// </summary>
public sealed class MqttSink(MqttSettings settings) : IEventSink, IAsyncDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    readonly SemaphoreSlim _gate = new(1, 1);

    public MqttSettings Settings => settings;

    string StatusTopic => $"{settings.TopicPrefix}/status";

    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await ConnectAsync(cancellationToken);
            await PublishAsync($"{settings.TopicPrefix}/events/{gameEvent.Type}", gameEvent.ToJson(), retain: false, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Connects with the settings and disconnects again; throws when the broker cannot be reached or refuses.</summary>
    public static async Task TestAsync(MqttSettings settings, CancellationToken cancellationToken)
    {
        await using var sink = new MqttSink(settings);
        await sink.ConnectAsync(cancellationToken);
    }

    async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected) return;
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
