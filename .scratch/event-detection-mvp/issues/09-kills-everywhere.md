# 09: Kills in every game mode

**What to build:** Kills are reported in regular multiplayer (for example Conquest) too, not only inside a REDSEC Match. A Kill outside a Match carries `matchId: null`.

**Blocked by:** 02, 03, 04

**Status:** resolved

- [x] A Kill Marker followed by a Kill Badge outside a Match yields a `kill` with `matchId: null`; `sequence` counts from 1 after each Match end
- [x] The multiplayer Kill Badge (skull without tick) is recognised; inside a REDSEC Match only the REDSEC Kill Badge (with tick) counts
- [x] Fixture clips from a live Conquest capture: two kills, one stretch without kills
- [x] REDSEC fixtures unchanged

## Comments

- Live capture of 10 min Conquest (2026-11-23): 3 kills, Kill Marker to Kill Badge 0.4-0.5 s, replay yields exactly those 3 kills. The REDSEC template scored only 0.80-0.86 on the multiplayer skull; the new `kill_badge_plain` scores 0.96-0.99.
- The plain skull also matches the REDSEC Knockdown Badge, so it is used only while no Match runs. If a REDSEC Match start is missed, a knockdown (also shown with a Kill Marker) could count as a kill.
- Not covered: a multiplayer multi-kill. The Score Popup then shows "DOPPEL-KILL" with one skull and summed points, so the skull count may not rise and the second kill may be missed. Needs a recording.
- Match boundaries in multiplayer (round start/end) are not detected.
- 2026-11-24, Team Deathmatch recording (12 min, 8 kills): two kills were missed. An explosive kill shows no Kill Marker; now a plain Kill Badge that opens a Score Popup seen empty for 1.5 s counts on its own. A double kill redraws the one skull in place; now the skull returning after a short dip counts as a new badge in multiplayer. Fixtures `mp_kill-explosive_tdm-1124`, `mp_double-kill_tdm-1124`, and `mp_kill-then-revenge_tdm-1124` (a "VERGELTUNG" ribbon redraws the skull without a kill). Open: whether a Squad-finished knockdown in multiplayer also opens a fresh popup without a Kill Marker; it would now count.
