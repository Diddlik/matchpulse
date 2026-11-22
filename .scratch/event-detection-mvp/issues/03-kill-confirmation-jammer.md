# 03: Kill confirmation via Kill Feed and the Jammer case

**What to build:** Kills are reported reliably and only for the Player. Without a Jammer, a Kill Feed entry with the Player's highlighted name confirms the Kill Marker. With a Jammer hiding the Kill Feed, the Kill Marker alone counts. Kills by the Squad never produce a `kill`.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] Fixture test: a Squad kill produces no `kill`
- [ ] Fixture test: a Kill with an active Jammer produces one `kill`
- [ ] Fixture test: two Kills in quick succession produce two `kill` events
- [ ] A Kill Marker without a confirming Kill Feed entry while no Jammer is active is handled as decided in 01, and logged
