# 17: Auto-update

**What to build:** MatchPulse keeps itself up to date from the GitHub releases, without the GitHub API: it reads `releases/latest/download/latest.json`, and installs a newer version silently when the game is not running.

**Blocked by:** 16

**Status:** resolved

- [x] The release workflow attaches `latest.json` (version, installer file name, SHA-256 of the installer) to each release
- [x] Settings "Check for updates automatically" (at start and every 24 h) and "Install updates automatically", both on by default, plus "Check now"; the Updates section shows the installed version and the last result
- [x] Auto-install downloads the installer, checks its SHA-256 (no install on a mismatch), waits until `bf6.exe` is not running, installs silently and restarts MatchPulse in the tray
- [x] Without auto-install, an available update shows on the Overview with an Install button
- [x] A portable (not installed) copy only shows the update with a link to the release page
- [x] A failed update keeps the current version and logs the error; after an update a Windows notification says "Updated to version X"; the version shows in the sidebar

## Comments

- `Updater` (manifest, download, SHA-256, installed check, silent installer start) and `UpdateService` (when to check and install) are covered by `UpdateTests` against an in-memory release site.
- Checked locally: installer 0.2.0 installed, then 0.2.1 run exactly as the updater runs it (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /relaunch=1`): files replaced in place, MatchPulse started again.
- The first release with the updater is the first that can be updated *to* from a later one; v0.1.0 has no updater and no `latest.json`.
- `MATCHPULSE_UPDATE_URL` points the updater at a test release site.
- Fixed on the way: the uninstaller stopped every process named MatchPulse (also a developer build); it now stops only the one running from its install folder.
