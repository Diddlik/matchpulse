# 04: `match_started` and `match_ended` with result and fallbacks

**What to build:** Consumers learn when a Match begins and ends and how it ended. `match_started` fires when the in-match HUD is confirmed. `match_ended` fires on the end-of-match screen with `result` `victory` or `defeat` and `reason: end_screen`. The Player's own death and spectating the Squad never end a Match. If the in-match HUD stays absent longer than the configured time (default 120 s, so a Jammer does not end the Match), a fallback `match_ended` with `result: unknown` and `reason: hud_lost` is sent. End-screen Cues use the colour masks from `docs/poc/01-report.md`.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] Fixture test: a full Match yields `match_started`, its `kill` events and `match_ended` in order, under one `matchId` with consecutive `sequence`
- [ ] Fixture test: victory and defeat produce the matching `result` (the victory fixture may depend on a recording requested in 01)
- [ ] Fixture test: death followed by spectating does not end the Match
- [ ] Fixture test: a Jammer hiding the counter bar does not end the Match
- [ ] Synthetic test: in-match HUD absent longer than the configured time yields `match_ended` with `result: unknown` and `reason: hud_lost`
- [ ] `kill` events outside a Match are not reported
