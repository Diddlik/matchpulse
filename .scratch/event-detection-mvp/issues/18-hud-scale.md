# 18: HUD scale setting

**What to build:** Players who changed the HUD size in Battlefield 6 set the same value in MatchPulse, so the Cue templates match their HUD.

**Blocked by:** 07

**Status:** needs-info

- [ ] Setting "HUD size" 50-150 % in steps of 5, default 100 %, as in the game
- [ ] HUD Cue templates are scaled by setting ÷ 115 (the templates were cut from recordings at 115 %) on top of the resolution scaling
- [ ] A diagnostic aid shows how well the templates match, so a wrong value can be spotted
- [ ] Fixture clips (recorded at 115 %) pass with the setting at 115

## Comments

- Finding (2026-11-23): on the English third-party footage the Kill Badge and the in-match HUD match best at template scale 1.0, exactly like the Player's own recordings at HUD 115 %. Either that player also uses 115 %, or the game's HUD size does not change these elements. The Player checks in game (HUD 100 %, one kill, compare); then this ticket is built, replaced by multi-scale matching, or dropped.
