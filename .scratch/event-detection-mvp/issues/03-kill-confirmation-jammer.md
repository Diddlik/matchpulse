# 03: Kill edge cases: Jammer, Squad finishes, counter flash fallback

**What to build:** Kills are reported for the Player only and are not lost in hard cases. A Kill under an active Jammer is still reported from the Kill Badge. When the Squad finishes an opponent the Player knocked down, the game credits a kill but no `kill` is reported. In REDSEC, when the Kill Badge is missed (for example against a very bright background), the kill counter flash shortly after a Kill Marker produces the `kill` instead. Kill detection must keep working without the REDSEC counter bar.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] Fixture test: `kill_jammer_1119-2047` yields one `kill` (with a Match already started), and its Knockdown Badge yields none
- [ ] Fixture test: `knock_squad-finishes` and `squad-kill-wipes-players-knock` yield no `kill`
- [ ] Fixture test: two Kills in quick succession produce two `kill` events (find or cut a clip from `samples/` if none exists)
- [ ] Counter flash fallback: a flash within about 1 s after a Kill Marker, with no Kill Badge seen, produces one `kill`; a flash without a preceding Kill Marker produces none; a flash with a Kill Badge does not produce a second `kill`
- [ ] With the counter flash Cue disabled, all Kill Badge fixtures still pass
