# 13: Live monitor

**What to build:** With the live monitor on, every kill gets a picture of the game screen, shown at once on the Live monitor page and in an optional window of its own for a second screen.

**Blocked by:** 12

**Status:** resolved

- [x] Switch on the Live monitor page (`liveMonitor` in the config); it applies from the next kill, without a game restart
- [x] Each `kill` captures the next screen image in full and saves it as a JPEG (at most 1920 px wide) to `%APPDATA%\MatchPulse\kills\`; the newest 300 are kept
- [x] The page shows the newest kill picture and the highlights of the current round (all kills outside a Match)
- [x] "Open in own window" opens a second window with the live monitor that never takes the focus
- [x] Covered by `KillPicturesTests` and `UiHubTests`; full-frame capture checked on the desktop

## Comments

- The picture is taken when the kill is confirmed, about 0.5 s after the Kill Marker: it shows the Kill Badge in the Score Popup and the Kill Feed, not the shot itself. Keeping a rolling buffer of full frames would show the moment of the shot but costs a full-screen copy at every check.
- Only one full-screen copy per kill; all other checks still copy only the Cue regions.
