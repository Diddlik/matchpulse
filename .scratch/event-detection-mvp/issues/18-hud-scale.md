# 18: HUD scale setting

**What to build:** Players who changed the HUD size in Battlefield 6 set the same value in MatchPulse, so the Cue templates match their HUD.

**Blocked by:** 07

**Status:** ready-for-agent

- [ ] Setting "HUD size" 50-150 % in steps of 5, default 100 %, as in the game
- [ ] HUD Cue templates are scaled by setting ÷ 115 (the templates were cut from recordings at 115 %) on top of the resolution scaling
- [ ] A diagnostic aid shows how well the templates match, so a wrong value can be spotted
- [ ] Fixture clips (recorded at 115 %) pass with the setting at 115
