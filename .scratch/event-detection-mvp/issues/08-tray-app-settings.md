# 08: Tray app and settings

**What to build:** MatchPulse runs as a tray app without an open main window. The tray icon shows whether Battlefield 6 is detected and whether a Match is running. A settings window lets the Player set the game language, webhooks and scripts, and view the log. Settings persist across restarts. The UI is available in German and English.

**Blocked by:** 02

**Status:** ready-for-agent

- [x] The tray icon shows one of three states: game not running, game running, Match running
- [ ] The settings window edits game language, webhook URLs and scripts, and changes take effect without a restart
- [x] Settings are stored locally and survive a restart
- [x] The log can be viewed from the settings window
- [x] UI language DE or EN, selectable

## Comments

**Ticket 08 work (criterion 2 stays open until 07 makes the Match Detector use the game language; webhooks and scripts already apply without a restart):**

- App is `net10.0-windows`, WinForms, `WinExe`. `MatchPulse` with no arguments starts the tray app; `MatchPulse replay <video> [--config <path>]` still works and writes to the calling terminal. Implicit `System.Drawing`/`System.Windows.Forms` usings are removed in the csproj so OpenCvSharp `Point` etc. stay unambiguous; UI files import them explicitly.
- Config (`%APPDATA%\MatchPulse\config.json`) gained `gameLanguage` and `uiLanguage` (`de`/`en`, defaults `de`/`en`), `Config.Save` and `Config.LoadOrDefault` (missing file gives defaults). `scripts` from 06 is never null now.
- Log: `FileLog` writes next to the console log to `%APPDATA%\MatchPulse\matchpulse.log`, rotating to `matchpulse.1.log` at 1 MB. The settings window has a Log tab (refresh button).
- Tray icon: grey = game not running, orange = game running, green = Match running. Tooltip shows the state in the UI language. Double-click or menu "Settings" opens the window.
- Saving settings writes the config and rebuilds the Event Dispatcher (`TrayApp.Dispatcher`), so webhook and script changes apply without a restart. Sinks come from `Program.Sinks(config, http)`.
- **Wired to 05:** the tray app runs `GameWatcher.Run` on a background thread (stopped on Exit) and dispatches through `TrayApp.Dispatcher`, read per Game Event because it is replaced on save. `GameWatcher.Run` got an optional `gameRunning` callback, which the tray passes as `AppStatus.SetGameRunning`. `match_started`/`match_ended` turn the icon green and back because `AppStatus` is one of the sinks. `MatchPulse live` still runs the Game Watcher in a terminal. `checksPerSecond` is read when the tray app starts.
- **For 04:** nothing to wire; once `match_started` is emitted, the green icon follows.
- **For 07 (game language):** the setting is `Config.GameLanguage`. To apply it without a restart, read `Config.GameLanguage` (from `TrayApp.Config`, passed into `GameWatcher.Run`) when building the Match Detector (for example per capture session), or rebuild it in `TrayApp.Apply`.
