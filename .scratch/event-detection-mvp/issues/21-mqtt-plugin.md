# 21: MQTT plugin

**What to build:** Game Events also go to an MQTT broker, so smart-home and automation tools (Home Assistant, Node-RED) can react without a webhook receiver.

**Blocked by:** 14

**Status:** resolved

- [x] Plugins page: broker address, port, TLS, optional user name and password, topic prefix, QoS, test connection; saved in the config (`mqtt`), applies at once
- [x] Every Game Event is published as its JSON to `<prefix>/events/<type>` (not retained)
- [x] `<prefix>/status` is `online` while connected and `offline` after closing, both retained; the Last Will sets `offline` if MatchPulse vanishes
- [x] A lost connection is made again with the next event; failures show on the Plugins page and Overview, with an optional Windows notification
- [x] Covered by `MqttSinkTests` against an in-process MQTTnet broker

## Comments

- Library: MQTTnet 5 (MIT). One persistent connection per sink; the Event Dispatcher now disposes a sink once its queue is done, so saving settings or exiting closes the connection cleanly with `offline`.
- Not built: Home Assistant MQTT discovery (sensors created automatically). Possible follow-up.
