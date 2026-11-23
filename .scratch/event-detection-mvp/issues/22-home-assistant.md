# 22: Home Assistant plugin

**What to build:** Home Assistant finds MatchPulse by itself: a "MatchPulse" device with sensors and automation triggers appears through MQTT discovery.

**Blocked by:** 21

**Status:** resolved

- [x] Plugins page: Home Assistant on/off and discovery prefix (default `homeassistant`), "Send to Home Assistant now"; needs the MQTT plugin and says so
- [x] Retained discovery configs: binary sensor "Match running", sensors kills / knockdowns / deaths in the match and last event, all with availability on `<prefix>/status`; device triggers for match_started, kill, knockdown, death, match_ended
- [x] Retained state topics `<prefix>/state/match|kills|knockdowns|deaths|last_event`, updated with every Game Event
- [x] With the plugin on, MatchPulse connects at start so the device appears before the first event; turned off, it removes the configs (empty retained messages)
- [x] Covered by `MqttSinkTests` against an in-process broker
- [ ] Checked against a real Home Assistant by the Player
