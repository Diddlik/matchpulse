# 11: Diagnostic mode

**What to build:** When the Player suspects a missed Kill, MatchPulse can save a picture of every unclear case, so the cause can be found after playing without recording whole sessions.

**Blocked by:** 09

**Status:** resolved

- [x] `"diagnostics": true` in the config turns it on, for `live`, the tray app and `replay`
- [x] A Kill Badge without a Kill Marker, and a Kill Marker whose window closes without a Kill Badge, each save one contact sheet JPEG to `%APPDATA%\MatchPulse\diagnostics\`
- [x] The sheet shows the last 2.5 s of the screen centre (crosshair, Score Popup, victim name) with times relative to the case, and the Kill Feed
- [x] Confirmed kills save nothing

## Comments

- Asked for after a live Conquest session gave 5 of 6 kills (2026-11-23): one Kill Badge without a Kill Marker at 12:36:03 and two Kill Markers without a new Kill Badge at 12:41:05 (probably a multiplayer double kill).
- Live capture copies two more screen areas (centre and Kill Feed) only while diagnostic mode is on. Unused Kill Markers are now dropped after their window, so the list no longer grows.
