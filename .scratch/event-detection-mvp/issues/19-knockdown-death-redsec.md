# 19: Knockdown and death in REDSEC

**What to build:** Two more Game Events in REDSEC: `knockdown` when the Player knocks an opponent down, and `death` when the Player is out of the Match.

**Blocked by:** 03, 04

**Status:** ready-for-agent

- [ ] `knockdown`: a Knockdown Badge shortly after the Player's Kill Marker, by the same rules as a Kill; knocking down and then finishing the same opponent gives `knockdown`, then `kill`
- [ ] `death`: the Player is eliminated for good (spectate view), without the opponent's name; being knocked down alone is not a death
- [ ] Fixture tests from the existing REDSEC samples; the OBS plugin and the window know the new event types
