# 03: Kill edge cases: Jammer, Squad finishes, counter flash fallback

**What to build:** Kills are reported for the Player only and are not lost in hard cases. A Kill under an active Jammer is still reported from the Kill Badge. When the Squad finishes an opponent the Player knocked down, the game shows the same Kill Badge (with "KILL BESTÄTIGT") but no Kill Marker before it, and no `kill` is reported: a Kill is a Kill Badge within about 1 s after a Kill Marker (finding from ticket 02). In REDSEC, when the Kill Badge is missed (for example against a very bright background), the kill counter flash shortly after a Kill Marker produces the `kill` instead. Kill detection must keep working without the REDSEC counter bar.

**Blocked by:** 02

**Status:** ready-for-agent

- [x] Fixture test: `kill_jammer_1119-2047` yields one `kill` (with a Match already started), and its Knockdown Badge yields none
- [x] Fixture test: `knock_squad-finishes` and `squad-kill-wipes-players-knock` yield no `kill`
- [ ] Fixture test: two Kills in quick succession produce two `kill` events (find or cut a clip from `samples/` if none exists)
- [x] Counter flash fallback: a flash within about 1 s after a Kill Marker, with no Kill Badge seen, produces one `kill`; a flash without a preceding Kill Marker produces none; a flash with a Kill Badge does not produce a second `kill`
- [x] With the counter flash Cue disabled, all Kill Badge fixtures still pass

## Comments

**Ticket 03 work (notes for 04 and 07):**

- Done: a Kill Badge yields a `kill` only within 1.5 s after a Kill Marker, and each Kill Marker confirms at most one `kill`. 1.5 s, not 1 s, because under the Jammer in `kill_jammer_1119-2047` the Score Popup shows the Kill Badge 1.2 s after the Kill Marker (it queues behind the Knockdown Badge). The one-kill-per-marker rule comes from a new fixture, `kill_then-wipe-credit_1105-2139`: the Player kills the last opponent of a squad, his earlier knockdown dies in the squad wipe, and the game adds a second Kill Badge without a Kill Marker. The same thing happens in `2026-11-15 21-46-18` at 446 s and `2026-11-15 22-31-05` at 37 s. Today these count as no `kill`, like `squad-kill-wipes-players-knock`. The Player should confirm this.
- Kill Marker Cue: `kill_marker_mask.png` matched against a red HSV mask (CCORR_NORMED, threshold 0.3, rising edge). Real markers score 0.35 to 0.77, and noise stays at 0.2 or below.
- Kill counter flash fallback (REDSEC): the skull is found by shape in the HUD band (score 0.7 or higher), more than half of its bright pixels must be green, and K is 2. The flash only counts within 1.5 s after an unused Kill Marker. The `kill` is held back until that window closes and is dropped if a Kill Badge comes first, so its `occurredAt` is the time of the flash but it is delivered up to about 1.5 s later. `new MatchDetector(killCounterFlash: false)` turns the fallback off. All fixtures pass with it on and with it off.
- Not met: two Kills in quick succession. I scanned every German sample with two or more kills for Kill Badges. Every time two Kill Badges came close together, the second one was a squad wipe credit (see above). The closest real pair is about 9 s apart in separate Score Popups (`2026-11-19 23-21-32` at 399.6/408.9 s, and the first of those is a bleed-out credit, `2026-11-06 20-58-57` at 2316.6/2325.9 s). Only the synthetic test covers this. Known limit: the detector remembers only the last Kill Marker, so two Kill Markers before both Kill Badges give one `kill`. A real recording of a quick double Kill is needed.
- Possible miss under a Jammer: in `2026-11-19 22-29-57` at about 467.8 s, during the Jammer gap, a Kill Badge appears 3.2 s after a knockdown's Kill Marker, with no Kill Marker in between. It cannot be checked because the Kill Feed is hidden. If it is the Player's own finish, it is missed today.
- For 04: the Kill Marker, Kill Badge and counter flash state now lives in `MatchDetector`. When Match boundaries arrive, reset it per Match, and keep a pending flash `kill` from carrying over into the next Match. Kill detection never uses the in-match HUD Cue. The counter flash shares the HUD search band (0.742, 0.042, 0.258, 0.059).
- For 07: three new Cues need scaling: `kill_marker` (mask template, 200×200 at 2560×1440), `kill_counter_flash`, and `kill_badge`. The red and green HSV bands are fixed in `MatchDetector` and do not depend on language.
