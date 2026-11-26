# 10: Match boundaries in multiplayer

**What to build:** In regular multiplayer (for example Conquest), a round is a Match: `match_started` when the round begins and `match_ended` (victory, defeat or unknown) when it ends, so multiplayer kills carry a `matchId`. The Match carries its mode and map.

**Blocked by:** 09

**Status:** resolved

- [x] Recording of a round end with its end screen (a victory and a defeat) and the start of the next round (see the 2026-11-26 comment)
- [x] Start Cue: the score bar at the top centre, matched as the share of team-blue pixels in the left score box and team-red pixels in the right one (both at least 0.06; rounds 0.13-0.78, REDSEC and menus 0.00). The scores change, so no template
- [x] End Cues: the emblem of the round-end banner, 2-3 s long. Its shape depends on the mode (Conquest shield, Team Deathmatch hexagon with a skull), its colour on the result, so both shapes are matched as a blue mask (victory) and a red mask (defeat). Banners score 0.95-0.97, everything else at most 0.59
- [x] Fixture clips: `mp_round-start_conquest-1125`, `mp_victory_conquest-1125`, `mp_defeat_conquest-1125`, `mp_defeat_tdm-1125`. The six older `mp_*` clips now start a multiplayer Match at 0.0 s; their kills are unchanged
- [x] Mode and map (ticket 25; not done: filling them in `match_ended` from the end screen when the briefing was missed): read with Windows OCR (`Windows.Media.Ocr`) from the top left of the briefing ("TEAM DEATHMATCH", "EUROPÄISCHES CASINO"), sent as `mode` and `map` in `match_started`; the end screen shows the same text top left and fills them in `match_ended` when the briefing was missed

## Comments

Postponed by the Player on 2026-11-23. Needs a capture that spans a round end and the next round start; MatchPulse can record it live while the Player plays.
- 2026-11-24: `samples/2026-11-24 19-58-16.mkv` holds a whole Team Deathmatch round: menu, briefing, spawn, play, defeat banner "NIEDERLAGE" at 11:44 and the end screen with "NIEDERLAGE" top right. No victory and no Conquest end yet.
- 2026-11-26: Ticket 25 reads the lobby header (mode and map) with Windows OCR and sends them in `match_started`; multiplayer lobbies are already read, so a multiplayer `match_started` gets them once its start Cue exists.
- 2026-11-26: The samples from 2026-11-25 hold six whole multiplayer rounds with their ends (times in s, from contact sheets every 2 s; refine when cutting clips):
  - Conquest victory: `2026-11-25 23-04-39`, "DER SIEG STEHT KURZ BEVOR" at about 1402, banner "SIEG" (blue emblem) at about 1422, end screen "SIEG" with 461 : 1000 → 230 : 0 at about 1428. The recording starts on the end screen of the round before, and the next round's HUD appears at about 300: a round end followed by a round start.
  - Conquest defeat: `2026-11-25 21-24-53`, banner "NIEDERLAGE" at about 510, end screen at about 540.
  - Team Deathmatch defeats: `2026-11-25 20-27-52` (banner about 664, end screen 076 : 100 about 668), `2026-11-25 20-39-41` (banner about 676, end screen 075 : 100 about 682), `2026-11-25 20-52-39` (banner about 774, end screen 097 : 100 about 782), `2026-11-24 19-58-16` (banner at 11:44).
  - Round starts after the briefing: `20-27-52` about 60, `20-39-41` about 90, `20-52-39` about 30, `23-04-39` about 300.
  - The multiplayer banner emblems differ from REDSEC (blue emblem for SIEG, red hexagon for NIEDERLAGE, not the gold laurel and the red shield), so new end Cues are needed. No Team Deathmatch victory yet; the Conquest one may serve if the banner is the same.
  - Watch out: inside a Match the detector runs the REDSEC Knockdown Badge and death Cues, and the plain multiplayer kill skull scores about 0.87 against the Knockdown Badge. A multiplayer Match must not turn those on (ticket 20).
- 2026-11-26: Built. A multiplayer Match turns on no REDSEC knockdown or death detection and keeps the multiplayer Kill Badge rules (plain skull, empty-popup and multi-kill exceptions); its kills carry the Match's `matchId`. Checked on all six multiplayer round ends in `samples/`: every start and every end found with the right result, the score bar never gone longer than 8.8 s inside a round (well under `hudLostAfterSeconds`), and no score bar in any REDSEC recording. No Team Deathmatch victory recorded yet: assumed to be the blue hexagon.
