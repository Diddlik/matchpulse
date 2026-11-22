Status: ready-for-agent

# Spec: Event detection MVP

## Problem Statement

I play Battlefield 6, mostly REDSEC (battle royale), and I want other tools to react to what happens in my matches: start and stop an OBS recording, mark highlights, run scripts. I can't get that information reliably today. Overwolf's game events depend on dev-mode access that Overwolf can revoke, and they would force monetization for any real release. BF6 writes no usable match log. Every reaction has to be triggered by hand.

## Solution

MatchPulse is a small Windows tray app. While `bf6.exe` runs, it watches the screen, recognises fixed HUD cues (match start, match end, the Kill Badge), and announces Game Events such as `match_started`, `kill` and `match_ended` to the Event Sinks I configure: a webhook or a script. MatchPulse records nothing itself. Recording control, for example through OBS, is a consumer of these events (ADR-0001, ADR-0002).

## User Stories

1. As a player, I want MatchPulse to notice when `bf6.exe` starts, so that detection runs without me starting it.
2. As a player, I want screen watching to stop when `bf6.exe` exits, so that MatchPulse costs nothing while I'm not playing.
3. As a player, I want a `match_started` Game Event when a Match begins, so that consumers can start recording or logging.
4. As a player, I want a `match_ended` Game Event when the end of a Match is recognised, so that consumers can stop recording.
5. As a player, I want `match_ended` to carry a result of `victory`, `defeat` or `unknown`, so that consumers can name or sort recordings.
6. As a player, I want my own death not to end the Match while I spectate my squad, so that the Match covers the whole game I took part in.
7. As a player, I want a `kill` Game Event each time I personally finish an opponent, and none when my Squad finishes an opponent I knocked down, so that consumers can mark highlights.
8. As a player, I want kills detected from the Kill Badge in the Score Popup, so that kills are still caught when a Jammer hides the Kill Feed and the counter bar.
9. As a player, I want one kill to produce exactly one `kill` event, so that consumers don't count it twice.
10. As a player, I want a Cue that flickers for a single frame to be ignored, so that false events don't reach my tools.
11. As a player, I want `match_ended` with `result: unknown` and `reason: game_exited` when the game closes mid-Match, so that consumers never wait forever for an end.
12. As a player, I want every Game Event to carry a `matchId`, so that consumers can group events by Match.
13. As a player, I want every Game Event to carry `occurredAt`, the local time with offset when the Cue was first seen, so that highlight timestamps line up with video.
14. As a player, I want a per-Match `sequence` number on every event, so that consumers can spot gaps or reordering.
15. As a player, I want detection to work at my screen resolution without manual calibration, so that setup is just "install and play".
16. As a player who plays BF6 in German or English, I want detection to work in either game language, so that the language I pick doesn't break it.
17. As a player, I want to set the game language in MatchPulse, so that text Cues use the right templates.
18. As a player, I want to configure webhook URLs that receive each Game Event as an HTTP POST with a JSON body, so that home automation or my own services can react.
19. As a player, I want to configure scripts that are run for each Game Event with the event JSON on stdin, so that I can drive OBS or anything else without MatchPulse knowing about it.
20. As a player, I want a failing webhook or script not to block detection or other sinks, so that one broken consumer doesn't lose events for the rest.
21. As a player, I want a tray icon showing whether the game is detected and whether a Match is running, so that I can see at a glance that MatchPulse works.
22. As a player, I want the UI in German and English, so that I can use it in my language.
23. As a player, I want settings saved locally and kept across restarts, so that I configure MatchPulse once.
24. As a player, I want a log of detected Cues, Game Events and sink failures, so that I can see why something was or wasn't detected.
25. As a player, I want screen watching to have no noticeable effect on my BF6 frame rate, so that playing isn't affected.
26. As a player, I want MatchPulse never to touch the game process, so that it is safe with EA anti-cheat.
27. As a developer, I want to replay recorded footage through detection and compare the Game Events with an expected list, so that I can check a game patch or a new Cue without playing.
28. As a developer, I want to add Cue templates for a new language without code changes, so that more languages are cheap to add.

## Implementation Decisions

- **Stack:** .NET 10, a single app project plus one test project. Vortice for DXGI Desktop Duplication, OpenCvSharp for template matching, WinForms only for the tray icon and settings window.
- **Modules:**
  - **Game Watcher:** detects whether `bf6.exe` is running and on which monitor. It starts and stops the capture loop.
  - **Frame Source:** delivers frames at 5–10 checks per second, cropped to the Cue regions. There are two implementations: live DXGI capture of the game's monitor, and replay that decodes recorded video files (MKV or MP4) directly and samples them at the same rate. There is no separate frame dump format. The detector cannot tell the two apart.
  - **Match Detector:** the single deep module and the single test seam. It takes timestamped frames and emits Game Events. Internally it holds the Cue matchers, the hysteresis and the Match state (`no_match` → `in_match` → `no_match`). It does no I/O and takes time from the frames, never from the clock.
  - **Event Dispatcher:** fans each Game Event out to all configured Event Sinks. Each sink is isolated: a failure or slow sink is logged and never blocks detection or other sinks.
  - **Event Sinks:** Webhook (HTTP POST, JSON) and Script (process started per event, event JSON on stdin).
  - **Tray UI and settings:** status, game language, sink configuration and log access, in DE and EN.
- **Cue definitions are data:** each Cue has a region in coordinates relative to the frame, one template per language (or one language-neutral template), a match threshold and a hysteresis count K. Language-neutral Cues (icons, shapes, colours) are preferred over text.
- **Resolution:** templates are authored at one reference resolution (2560×1440) and scaled to the actual frame size. The MVP supports 16:9 at the default HUD scale only.
- **Hysteresis:** a Cue counts as seen after K consecutive matching checks. K is set per Cue: higher for match start and end, lower for short-lived Cues. One Kill Badge produces one `kill`, even while it stays visible across many checks.
- **Kill detection:** the Kill Badge is the primary Cue. Each new Kill Badge added to the Score Popup is one `kill`; Knockdown Badges never produce one. The Kill Badge stays visible under a Jammer. The Kill Marker is only a helper signal, because it looks the same on knockdowns. The Kill Feed is not used, because its colours depend on game settings. In REDSEC, the kill counter flash is a secondary source: it produces a `kill` only when a Kill Marker came shortly before and no Kill Badge was seen, for example against a very bright background. A kill credited because the Squad finished the Player's knockdown is not a `kill`.
- **Match boundaries:** a Match starts when the in-match HUD Cue is confirmed and ends on a confirmed end-of-match Cue. An end recognised from the end-of-match screen carries `reason: end_screen`. If the in-match HUD has been absent for a configurable time (`hudLostAfterSeconds` in `config.json`, default 120 s, longer than a Jammer), a fallback `match_ended` with `result: unknown` and `reason: hud_lost` is sent. Player death (down screen, spectating) never ends a Match. A game exit during a Match ends it with `reason: game_exited`; stopping MatchPulse during a Match ends it with `result: unknown` and `reason: stopped`, delivered to the Event Sinks before MatchPulse exits. Because many Matches are left before the end screen, `result: unknown` is common.
- **Game Event contract (JSON):** `type` (`match_started` | `kill` | `match_ended`), `matchId` (unique per Match), `sequence` (per Match, starting at 1), `occurredAt` (ISO 8601 local time with offset, time the Cue was first seen), `data` (for `match_ended`: `result` and `reason`). Field names are part of the public contract for consumers.
- **Prerequisite (POC):** before full implementation, start from the Sample Recordings in `samples/`, add further real REDSEC matches as needed in DE and EN, with and without a Jammer, and confirm which Cues reliably mark match start, match end and result, and that DXGI capture works with the game's display mode. The recorded footage becomes the test fixtures.

## Testing Decisions

- **One seam:** the Match Detector. Tests feed it a timestamped frame sequence and assert the exact list of Game Events, with type, order, sequence and result. They never assert internal state, cue scores or which matcher fired.
- **Sample Recordings:** full REDSEC matches recorded with OBS live in `samples/` in the repo root. They are H.264 MKV files at 2560×1440 and 60 fps, with the game in German, between about 6 and 20 minutes long, about 23 GB in total. They are the starting material for kill detection and the POC. Two further recordings are third-party REDSEC gameplay videos in English, MP4 at 1920×1080 and 59.94 fps, about 22 and 36 minutes, played by a different player. They cover English and a second resolution. They are too large for version control and stay local only. The first POC step lists what they cover and what further recordings are missing, for example English, a Jammer or a victory.
- **Fixtures:** short clips of a few seconds, re-encoded at reduced quality (CRF 26, 30 fps, no audio) to stay small, cut from the Sample Recordings and POC recordings, checked into the repo with an expected event list per clip. Cover at least a full Match won and lost, death followed by spectating, a Jammer, DE and EN, two resolutions, and a game exit mid-Match.
- **Synthetic cases:** short hand-built frame sequences for hysteresis edge cases: a single flicker frame, a Kill Badge held across many checks, two kills close together, and the HUD-lost fallback.
- **Not unit-tested:** DXGI capture, Game Watcher, Event Sinks and UI. They are thin and are checked by running the app.
- **Prior art:** none, the repo is empty.

## Out of Scope

- Recording video or controlling OBS directly. OBS is driven through a Script sink or a later dedicated consumer, which also owns the post-roll delay and the rule of never stopping a manual recording.
- Game modes other than REDSEC, including regular multiplayer. Kill detection must not depend on the REDSEC counter bar, so it can be reused there later.
- A `knockdown` event (planned next, the Knockdown Badge makes it cheap), own death, squad events and other events beyond `match_started`, `kill` and `match_ended`.
- Recognising the game mode. A later option is to read it from the start screen.
- MQTT and RPC sinks. MQTT is the planned next sink.
- Ultrawide or other aspect ratios, non-default HUD scale, and languages other than DE and EN.
- OCR, game log files, Overwolf, and anything that reads game memory or injects into the game.
- Cloud services and sending data off the machine except to sinks the user configures.

## Further Notes

- The POC findings are in `docs/poc/01-report.md`. Cue regions, thresholds and templates are taken from there.
- Template matching is tied to HUD layout. Any BF6 patch can break a Cue, so the replay fixtures double as a regression check after updates.
- See `GLOSSARY.md` for terms and `docs/adr/` for the Overwolf and scope decisions.
