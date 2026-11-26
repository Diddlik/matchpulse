# 10: Match boundaries in multiplayer

**What to build:** In regular multiplayer (for example Conquest), a round is a Match: `match_started` when the round begins and `match_ended` (victory, defeat or unknown) when it ends, so multiplayer kills carry a `matchId`. The Match carries its mode and map.

**Blocked by:** 09

**Status:** needs-info

- [ ] Recording of a round end with its end screen (a victory and a defeat) and the start of the next round
- [ ] Start Cue, for example the ticket bar at the top centre with the flags
- [ ] End Cues from the multiplayer end screen
- [ ] Fixture clips for start, victory and defeat
- [ ] Mode and map (reading done in ticket 25; still open: `match_ended` from the end screen when the briefing was missed): read with Windows OCR (`Windows.Media.Ocr`) from the top left of the briefing ("TEAM DEATHMATCH", "EUROPÄISCHES CASINO"), sent as `mode` and `map` in `match_started`; the end screen shows the same text top left and fills them in `match_ended` when the briefing was missed

## Comments

Postponed by the Player on 2026-11-23. Needs a capture that spans a round end and the next round start; MatchPulse can record it live while the Player plays.
- 2026-11-24: `samples/2026-11-24 19-58-16.mkv` holds a whole Team Deathmatch round: menu, briefing, spawn, play, defeat banner "NIEDERLAGE" at 11:44 and the end screen with "NIEDERLAGE" top right. No victory and no Conquest end yet.
- 2026-11-26: Ticket 25 reads the lobby header (mode and map) with Windows OCR and sends them in `match_started`; multiplayer lobbies are already read, so a multiplayer `match_started` gets them once its start Cue exists.
