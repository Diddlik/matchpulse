# MatchPulse is an event source, not a recorder

The original spec was an OBS auto-recorder. We narrowed it: MatchPulse only detects game events and delivers them to event sinks such as a webhook, MQTT or a script. Recording control through OBS becomes one optional consumer instead of the core. This keeps detection testable on its own and lets other tools, like a highlight extractor or home automation, use the same events. OBS-specific rules (post-roll delay, never stopping a manual recording) belong to that OBS consumer, not to detection.
