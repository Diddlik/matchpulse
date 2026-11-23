# 24: Turn MatchPulse on and off, with a hotkey and an on-screen notice

**What to build:** The Player pauses and resumes MatchPulse with a checkbox, the tray menu or a system-wide hotkey; a short notice above all windows says "MatchPulse activated / deactivated".

**Blocked by:** 12

**Status:** resolved

- [x] Setting "MatchPulse active" (`active`, kept across restarts): off means no screen capture and no Game Events; a running Match ends with `match_ended` reason `stopped`
- [x] System-wide hotkey (`toggleHotkey`, default Ctrl+Alt+M), set in Settings by pressing the combination; empty turns it off; a combination taken by another program is logged
- [x] Overlay notice at the top of the screen under the mouse pointer: topmost, never takes the focus, click-through, fades out after about 1.5 s
- [x] Tray menu item "Active" with the hotkey, paused tray icon and tooltip; Overview notice with an Activate button while paused
- [x] Covered by `HotkeyTests`; hotkey registration, a synthetic key press and the overlay checked on the developer PC

## Comments

- The hotkey uses RegisterHotKey: Windows reports only that one combination, no other keys are seen.
- Windows shows no other window above a game in exclusive fullscreen; the notice works over borderless and windowed modes.
