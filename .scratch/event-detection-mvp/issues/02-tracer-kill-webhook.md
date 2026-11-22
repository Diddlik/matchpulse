# 02: Tracer bullet: `kill` from a sample delivered by webhook

**What to build:** Replaying a recorded video through MatchPulse produces a `kill` Game Event for each Kill of the Player, and that event arrives at a configured webhook as an HTTP POST with the event JSON. This is the first complete path through every part: video replay, Match Detector with hysteresis, Event Dispatcher and Webhook sink. A Kill is recognised from the Kill Badge in the Score Popup (see `docs/poc/01-report.md` and `GLOSSARY.md`).

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Kill Badge and Knockdown Badge templates cut from the samples into `fixtures/cues/` (see the addendum in `docs/poc/01-report.md`)
- [ ] Replay decodes an MKV or MP4 file directly and samples it by timestamp at a configurable 5–10 checks per second (default 10)
- [ ] Each new Kill Badge in the Score Popup produces exactly one `kill`; a Kill Badge that stays visible across many checks does not repeat it, and a single flicker frame produces none
- [ ] A Knockdown Badge or a Kill Marker alone never produces a `kill`
- [ ] Event JSON carries `type`, `matchId`, `sequence`, `occurredAt` (local time with offset, when the Cue was first seen) and `data`
- [ ] Webhook URLs come from a local config file, and every event is POSTed to each of them
- [ ] A failing webhook is logged and does not stop detection
- [ ] Cue detections and Game Events are logged
- [ ] Fixture tests: `kill_direct`, `kill_finish-downed` and `kill_knock-then-finish` each yield exactly their expected `kill` events (a Match may be treated as already started for this ticket; `match_started` itself is ticket 04)
