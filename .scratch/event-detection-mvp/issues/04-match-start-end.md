# 04: `match_started` and `match_ended` with result and fallbacks

**What to build:** Consumers learn when a Match begins and ends and how it ended. `match_started` fires when the in-match HUD is confirmed. `match_ended` fires on the end-of-match screen with `result` `victory` or `defeat` and `reason: end_screen`. The Player's own death and spectating the Squad never end a Match. If the in-match HUD stays absent longer than the configured time (default 120 s, so a Jammer does not end the Match), a fallback `match_ended` with `result: unknown` and `reason: hud_lost` is sent. End-screen Cues use the colour masks from `docs/poc/01-report.md`.

**Blocked by:** 02

**Status:** resolved

- [x] Fixture test: a full Match yields `match_started`, its `kill` events and `match_ended` in order, under one `matchId` with consecutive `sequence`
- [x] Fixture test: victory and defeat produce the matching `result` (the victory fixture may depend on a recording requested in 01)
- [x] Fixture test: death followed by spectating does not end the Match
- [x] Fixture test: a Jammer hiding the counter bar does not end the Match
- [x] Synthetic test: in-match HUD absent longer than the configured time yields `match_ended` with `result: unknown` and `reason: hud_lost`
- [x] `kill` events outside a Match are not reported

## Comments

**Ticket 04 done (notes for 05, 07, 08):**

- New Cues in `Cue.cs`: `in_match_hud` (grayscale, threshold 0.7, K 5, HUD search band), `end_victory` and `end_defeat` (colour masks, CCORR_NORMED, threshold 0.8, K 3). Mask bands: gold H 20–36, red H ≤ 18 or ≥ 170, both S > 90 and V > 150. On the end screens these score 0.92–0.98, on all gameplay clips 0.58 or below.
- A Match starts on a confirmed in-match HUD (`occurredAt` is the first matching check). It ends on an end screen (`reason: end_screen`) or after the in-match HUD has been absent longer than `hudLostAfter` (constructor parameter, default 120 s; it is not in `config.json` yet), with `result: unknown`, `reason: hud_lost`. In that case `occurredAt` is the check where the time ran out. `GameExited` uses the same Match state. When the in-match HUD comes back after `hud_lost`, a new Match starts.
- Kills count only inside a Match. All kill state resets when a Match starts. A held counter flash `kill` is sent before `match_ended`.
- Each unused Kill Marker confirms one later Kill Badge within 1.5 s, newest first (`Two_Kill_Markers_before_two_Kill_Badges_yield_two_kills`). Newest first keeps `kill_then-wipe-credit` at one kill. A squad wipe credit stays not a `kill`.
- Full-Match fixture test: `match-start`, `kill_direct`, `kill_finish-downed` and `victory` replayed one after another by one Match Detector. No new clips were needed. A replay of the whole `2026-11-08 21-57-29` gives one Match, 8 Kill Badges and the victory, with no false end.
- `kill_jammer_1119-2047` has no in-match HUD, so the fixture test starts a Match with synthetic HUD frames before the clip.
