# 17: Auto-update

**What to build:** MatchPulse keeps itself up to date from the GitHub releases, without the GitHub API: it reads `releases/latest/download/latest.json`, and installs a newer version silently when the game is not running.

**Blocked by:** 16

**Status:** ready-for-agent

- [ ] The release workflow attaches `latest.json` (version, installer file name, SHA-256 of the installer) to each release
- [ ] Settings "Check for updates automatically" (at start and every 24 h) and "Install updates automatically", both on by default, plus "Check now"; the Updates section shows the installed version and the last result
- [ ] Auto-install downloads the installer, checks its SHA-256 (no install on a mismatch), waits until `bf6.exe` is not running, installs silently and restarts MatchPulse in the tray
- [ ] Without auto-install, an available update shows on the Overview with an Install button
- [ ] A portable (not installed) copy only shows the update with a link to the release page
- [ ] A failed update keeps the current version and logs the error; after an update a Windows notification says "Updated to version X"; the version shows in the sidebar
