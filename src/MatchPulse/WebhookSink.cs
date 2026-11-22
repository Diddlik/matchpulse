using System.Text;

namespace MatchPulse;

/// <summary>POSTs each Game Event as JSON to one URL. Failures throw and are logged by the Event Dispatcher.</summary>
public sealed class WebhookSink(HttpClient http, Uri url) : IEventSink
{
    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        using var content = new StringContent(gameEvent.ToJson(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public override string ToString() => $"webhook {url}";
}
