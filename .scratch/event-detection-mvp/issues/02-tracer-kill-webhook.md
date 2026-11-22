# 02: Tracer bullet: `kill` from a sample delivered by webhook

**What to build:** Replaying a recorded video through MatchPulse produces a `kill` Game Event for each Kill of the Player, and that event arrives at a configured webhook as an HTTP POST with the event JSON. This is the first complete path through every part: video replay, Match Detector with hysteresis, Event Dispatcher and Webhook sink. A Kill is recognised from the Kill Badge in the Score Popup (see `docs/poc/01-report.md` and `GLOSSARY.md`).

**Blocked by:** 01

**Status:** resolved

- [x] Kill Badge and Knockdown Badge templates cut from the samples into `fixtures/cues/` (see the addendum in `docs/poc/01-report.md`)
- [x] Replay decodes an MKV or MP4 file directly and samples it by timestamp at a configurable 5–10 checks per second (default 10)
- [x] Each new Kill Badge in the Score Popup produces exactly one `kill`; a Kill Badge that stays visible across many checks does not repeat it, and a single flicker frame produces none
- [x] A Knockdown Badge or a Kill Marker alone never produces a `kill`
- [x] Event JSON carries `type`, `matchId`, `sequence`, `occurredAt` (local time with offset, when the Cue was first seen) and `data`
- [x] Webhook URLs come from a local config file, and every event is POSTed to each of them
- [x] A failing webhook is logged and does not stop detection
- [x] Cue detections and Game Events are logged
- [x] Fixture tests: `kill_direct`, `kill_finish-downed` and `kill_knock-then-finish` each yield exactly their expected `kill` events (a Match may be treated as already started for this ticket; `match_started` itself is ticket 04)

## Comments

**Ticket 02 done (notes for 03–08):**

- Solution: `MatchPulse.slnx`, app `src/MatchPulse` (net10.0 console for now: `MatchPulse replay <video> [--config <path>]`, default config `%APPDATA%\MatchPulse\config.json`, e.g. `{ "webhooks": ["http://localhost:8080/bf6"], "checksPerSecond": 10 }`), tests `tests/MatchPulse.Tests` (xUnit). Ticket 08 will need `net10.0-windows` for WinForms.
- Modules: `ReplayFrameSource` (Frame Source, samples by timestamp), `MatchDetector` (the test seam: `Process(Frame)` returns Game Events), `EventDispatcher` (one queue and worker per sink, failures logged), `IEventSink` + `WebhookSink`. Ticket 06 adds a Script sink by implementing `IEventSink`.
- **Cues are data:** a `Cue` record in `src/MatchPulse/Cue.cs` holds name, template file, relative search region, threshold and K. Add a new Cue there as a static instance, put its template in `fixtures/cues/` (all `*.png` there are copied to `cues/` next to the exe) and use it from `MatchDetector`. Templates are cut at 2560×1440 and not scaled yet (ticket 07).
- Kill Badge: `fixtures/cues/kill_badge.png`, Knockdown Badge: `fixtures/cues/knockdown_badge.png` (cut from `2026-11-08 21-57-29` at 513.5 s, x 860/904, y 920). Grayscale TM_CCOEFF_NORMED in band (0.30, 0.62, 0.20, 0.06): Kill Badge 0.92–1.0, Knockdown Badge against the Kill Badge template ≤ 0.75, threshold 0.8. The detector counts Kill Badges in the Score Popup: each rise confirmed for K = 2 checks emits one `kill` per new badge; a drop must last 5 checks before it is accepted, so badges missed on a bright background are not counted twice.
- The Kill Badge settles 0.4–0.6 s after the Kill Marker, so `occurredAt` is that much later than the offsets in `.expected.json` (measured on the Kill Marker). The fixture tests allow ±0.75 s.
- **Contradicts the addendum, affects 03:** a Kill Badge (identical icon, "KILL BESTÄTIGT", +50) also appears when the Squad finishes the Player's knockdown, in both `knock_squad-finishes_1108-2157` (7.9 s) and `squad-kill-wipes-players-knock_1108-2157` (2.4 s). Today both yield a `kill`. Replaying the full `2026-11-08 21-57-29` yields 8 kills, equal to the counter, including those two. Ticket 03 needs to tell them apart, for example by requiring a Kill Marker within about 1 s before the Kill Badge (Kill Marker → Kill Badge was 0.4–0.6 s on all own kills; neither Squad clip has a Kill Marker then).
- Until ticket 04, `MatchDetector` treats a Match as running from its first frame (one `matchId`, `sequence` from 1).
