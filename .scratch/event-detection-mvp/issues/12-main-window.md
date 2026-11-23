# 12: Main window from the UI template

**What to build:** The Player opens one MatchPulse window from the tray that shows the state, the latest Game Events, the health of each target, problems, settings, diagnostic snapshots and the log, using the designed UI template (`docs/ui-brief.md`, template from Claude Design).

**Blocked by:** 08, 11

**Status:** resolved

- [x] Tray double-click or "Open MatchPulse" opens the window; `MatchPulse --show` starts with it open
- [x] Overview, Events, Targets, Settings, Diagnostics and Log pages work on live data, in German and English, light and dark
- [x] Targets: add, edit, remove, and send a test event (`"type": "test"`); last success, last failure and error per target
- [x] Settings save to the config; webhooks and scripts apply at once, detection settings from the next game start
- [x] Diagnostic snapshots are listed with their pictures; the folder can be opened and cleared
- [x] Optional Windows notification when a target starts failing or screen capture fails; clicking it opens the matching page
- [x] Host side covered by `UiHubTests`

## Comments

- The template lives in `src/MatchPulse/ui/` and is copied next to the exe. Opening `ui/index.html` in a browser still shows the mock data, so the design can be iterated without the app.
- Live monitor (kill pictures) and plugins (OBS control, audio ducking) are designed but not built: their pages are hidden in the app and shown only in the browser preview. Follow-up tickets when wanted.
- New config keys: `theme` (system/light/dark) and `notifyOnProblems` (default true).
- Test events use `"type": "test"`, `matchId: null`, `sequence: 0`, so receivers can ignore them.
