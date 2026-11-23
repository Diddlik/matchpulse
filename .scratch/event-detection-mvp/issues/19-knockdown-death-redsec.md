# 19: Knockdown and death in REDSEC

**What to build:** Two more Game Events in REDSEC: `knockdown` when the Player knocks an opponent down, and `death` when the Player is out of the Match.

**Blocked by:** 03, 04

**Status:** resolved

- [x] `knockdown`: a Knockdown Badge shortly after the Player's Kill Marker, by the same rules as a Kill; knocking down and then finishing the same opponent gives `knockdown`, then `kill`
- [x] `death`: the Player is eliminated for good (spectate view), without the opponent's name; being knocked down alone is not a death
- [x] Fixture tests from the existing REDSEC samples; the OBS plugin and the window know the new event types

## Comments

- Knockdown: a Knockdown Badge after an unused Kill Marker is held until that marker's 1.5 s window closes, then sent with the time of the badge. If a Kill Badge takes the same marker first, the knockdown is dropped: under a Jammer the Score Popup queued the Knockdown Badge before the Kill Badge of one Kill Marker (`kill_jammer_1119-2047`). A held knockdown with an older marker is sent before the kill that follows it. Inside a Match only: in multiplayer the plain kill skull scores ~0.87 against the Knockdown Badge.
- Fixtures: knockdowns in `kill_then-wipe-credit_1105-2139` (Kill Feed confirms) and `knock_squad-finishes_1108-2157`. In `kill_knock-then-finish_1105-2236` the knockdown Kill Marker is red for one video frame and falls between checks: listed as a known miss.
- Death: the spectate label ("BEOBACHTE", `cues/de/spectate_label.png`), once per elimination, re-armed after 5 s without it. Only German so far: an English recording with "Spectating" is needed for `cues/en/`.
- Kill events now carry `data.kills` (the Player's kills in the Match so far, or since the last Match outside one), and match_ended carries the total; the OBS `{kills}` placeholder reads it, because not every event in a Match is a kill any more.
- Full replay of `2026-11-08 21-57-29`: 5 kills (same as before this ticket), 3 knockdowns.
