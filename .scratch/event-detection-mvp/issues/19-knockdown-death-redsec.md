# 19: Knockdown and death in REDSEC

**What to build:** Two more Game Events in REDSEC: `knockdown` when the Player knocks an opponent down, and `death` when the Player is out of the Match.

**Blocked by:** 03, 04

**Status:** resolved

- [x] `knockdown`: a Knockdown Badge shortly after the Player's Kill Marker, by the same rules as a Kill; knocking down and then finishing the same opponent gives `knockdown`, then `kill`
- [x] `death`: the Player is eliminated for good (spectate view), without the opponent's name; being knocked down alone is not a death
- [x] Fixture tests from the existing REDSEC samples; the OBS plugin and the window know the new event types

## Comments

- Knockdown: a Knockdown Badge after an unused Kill Marker is held until that marker's 1.5 s window closes, then sent with the time of the badge. If a Kill Badge takes the same marker first, the knockdown is dropped: under a Jammer the Score Popup queued the Knockdown Badge before the Kill Badge of one Kill Marker (`kill_jammer_1119-2047`). A held knockdown with an older marker is sent before the kill that follows it. A Knockdown Badge without a Kill Marker is a knockdown too (2026-11-25 samples: the knockdown marker is often red for only 2-3 of 60 video frames, e.g. through a sniper scope, so 10 checks/s miss it), held the same way from the badge; not when it appears within 1.5 s after a Kill Badge, nor when it was already showing as the Match started. Inside a Match only: in multiplayer the plain kill skull scores ~0.87 against the Knockdown Badge.
- Knockdown is the kill (Player's decision, 2026-11-26): the Player wants the trigger at the moment of knocking down, not at the later credit. Each knockdown sends `knockdown` and then `kill` with the same time. The opponent's later Kill Badge sends nothing: a Kill Badge without a Kill Marker (Squad finish, bleed-out) closes the oldest open knockdown; a Kill Badge with a Kill Marker while a knockdown is open is held 1.5 s. If a Kill Badge credit without a marker follows in that time, the held kill was an outright kill wiping the knocked opponent's Squad and is sent, unless two knockdowns are open (the Player finished one, the other died in the wipe: 22-28-19 at 701 s); otherwise it finished the knockdown. Open knockdowns are forgotten after 60 s (revived; one was finished 34 s after the knockdown). Checked in the 2026-11-25 samples via the Kill Feed: the finishes folded into knockdowns were the same opponents (RW66, vintage-family34, SVINK86).
- Fixtures: knockdowns in `kill_then-wipe-credit_1105-2139` (Kill Feed confirms) and `knock_squad-finishes_1108-2157`. In `kill_knock-then-finish_1105-2236` the knockdown Kill Marker is red for one video frame and falls between checks; the Knockdown Badge alone gives the knockdown.
- Death: the spectate label ("BEOBACHTE", `cues/de/spectate_label.png`), once per elimination, re-armed after 5 s without it. Only German so far: an English recording with "Spectating" is needed for `cues/en/`.
- Kill events now carry `data.kills` (the Player's kills in the Match so far, or since the last Match outside one), and match_ended carries the total; the OBS `{kills}` placeholder reads it, because not every event in a Match is a kill any more.
- Full replay of `2026-11-08 21-57-29`: 5 kills (same as before this ticket), 3 knockdowns.
