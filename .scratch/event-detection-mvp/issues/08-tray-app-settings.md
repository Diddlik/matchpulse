# 08: Tray app and settings

**What to build:** MatchPulse runs as a tray app without an open main window. The tray icon shows whether Battlefield 6 is detected and whether a Match is running. A settings window lets the Player set the game language, webhooks and scripts, and view the log. Settings persist across restarts. The UI is available in German and English.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] The tray icon shows one of three states: game not running, game running, Match running
- [ ] The settings window edits game language, webhook URLs and scripts, and changes take effect without a restart
- [ ] Settings are stored locally and survive a restart
- [ ] The log can be viewed from the settings window
- [ ] UI language DE or EN, selectable
