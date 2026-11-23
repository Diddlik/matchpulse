# 10: Match boundaries in multiplayer

**What to build:** In regular multiplayer (for example Conquest), a round is a Match: `match_started` when the round begins and `match_ended` (victory, defeat or unknown) when it ends, so multiplayer kills carry a `matchId`.

**Blocked by:** 09

**Status:** needs-info

- [ ] Recording of a round end with its end screen (a victory and a defeat) and the start of the next round
- [ ] Start Cue, for example the ticket bar at the top centre with the flags
- [ ] End Cues from the multiplayer end screen
- [ ] Fixture clips for start, victory and defeat

## Comments

Postponed by the Player on 2026-11-23. Needs a capture that spans a round end and the next round start; MatchPulse can record it live while the Player plays.
