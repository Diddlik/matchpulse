using System.Text.Json.Nodes;

namespace MatchPulse;

/// <summary>
/// Home Assistant MQTT discovery: the retained config messages that make a "MatchPulse" device appear in Home Assistant,
/// with sensors on MatchPulse's state topics and device triggers on its event topics.
/// </summary>
public static class HomeAssistant
{
    public static readonly string[] Triggers = ["match_started", "kill", "knockdown", "death", "match_ended"];

    /// <summary>The discovery topics and their config payloads for a topic prefix.</summary>
    public static IEnumerable<(string Topic, JsonObject Config)> Discovery(MqttSettings mqtt, HomeAssistantSettings ha, string version)
    {
        var node = NodeId(mqtt.TopicPrefix);
        var p = mqtt.TopicPrefix;
        JsonObject Device() => new()
        {
            ["identifiers"] = new JsonArray(node),
            ["name"] = "MatchPulse",
            ["manufacturer"] = "MatchPulse",
            ["model"] = "Battlefield 6 event source",
            ["sw_version"] = version,
        };
        JsonObject Entity(string id, string name, string state, string? icon = null)
        {
            var config = new JsonObject
            {
                ["name"] = name,
                ["unique_id"] = $"{node}_{id}",
                ["object_id"] = $"{node}_{id}",
                ["state_topic"] = $"{p}/state/{state}",
                ["availability_topic"] = $"{p}/status",
                ["device"] = Device(),
            };
            if (icon is not null) config["icon"] = icon;
            return config;
        }

        var match = Entity("match", "Match running", "match", "mdi:sword-cross");
        match["payload_on"] = "ON";
        match["payload_off"] = "OFF";
        yield return ($"{ha.DiscoveryPrefix}/binary_sensor/{node}/match/config", match);

        foreach (var (id, name, icon) in new[] { ("kills", "Kills", "mdi:skull-crossbones"), ("knockdowns", "Knockdowns", "mdi:arrow-down-bold"), ("deaths", "Deaths", "mdi:coffin") })
        {
            var counter = Entity(id, name, id, icon);
            counter["state_class"] = "measurement";
            yield return ($"{ha.DiscoveryPrefix}/sensor/{node}/{id}/config", counter);
        }
        yield return ($"{ha.DiscoveryPrefix}/sensor/{node}/last_event/config", Entity("last_event", "Last event", "last_event", "mdi:history"));

        foreach (var type in Triggers)
            yield return ($"{ha.DiscoveryPrefix}/device_automation/{node}/{type}/config", new JsonObject
            {
                ["automation_type"] = "trigger",
                ["topic"] = $"{p}/events/{type}",
                ["type"] = type,
                ["subtype"] = "matchpulse",
                ["device"] = Device(),
            });
    }

    /// <summary>Home Assistant ids may hold letters, digits and underscores only.</summary>
    public static string NodeId(string topicPrefix) =>
        string.Concat(topicPrefix.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')).Trim('_') is { Length: > 0 } id ? id : "matchpulse";
}

/// <summary>Home Assistant plugin: publish MQTT discovery through the MQTT plugin's connection.</summary>
public sealed record HomeAssistantSettings(bool Enabled = false, string DiscoveryPrefix = "homeassistant");
