# 16: Installer and GitHub release

**What to build:** Players install MatchPulse from a GitHub release with a normal Windows installer; a version tag builds and publishes the release by itself.

**Blocked by:** 12

**Status:** resolved

- [x] Inno Setup installer (`installer/MatchPulse.iss`): per user, no admin rights, German/English, Start menu entry, optional desktop icon, optional autostart in the tray, start after install; uninstall removes the app and its autostart and keeps `%APPDATA%\MatchPulse`
- [x] Self-contained win-x64 publish (no .NET install needed); app icon
- [x] Only one tray app per user: a second start opens the running one's window
- [x] GitHub Actions (`.github/workflows/build.yml`): tests on every push and pull request; a `v*` tag also publishes, builds the installer and a portable zip, and creates the release
- [x] Checked locally: installer built, silent install, start, second start, silent uninstall without leftovers
