# 02: Tracer bullet: `kill` from a sample delivered by webhook

**What to build:** Replaying a recorded video through MatchPulse produces a `kill` Game Event for each Kill of the Player, and that event arrives at a configured webhook as an HTTP POST with the event JSON. This is the first complete path through every part: video replay, Match Detector with hysteresis, Event Dispatcher and Webhook sink.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Replay decodes an OBS MKV file directly and samples it at a configurable 5–10 checks per second
- [ ] One Kill Marker appearance produces exactly one `kill`; a marker visible across many checks does not repeat it, and a single flicker frame produces none
- [ ] Event JSON carries `type`, `matchId`, `sequence`, `occurredAt` (local time with offset, when the Cue was first seen) and `data`
- [ ] Webhook URLs come from a local config file, and every event is POSTed to each of them
- [ ] A failing webhook is logged and does not stop detection
- [ ] Cue detections and Game Events are logged
- [ ] Fixture test: a kill clip from 01 replayed through the Match Detector yields exactly the expected Game Event list
