# 14: OBS plugin

**What to build:** MatchPulse controls OBS directly over OBS WebSocket (v5, built into OBS 28+), with rules "when Game Event, do action", set on the Plugins page.

**Blocked by:** 12

**Status:** ready-for-human

- [x] Actions: start/stop recording, start/stop replay buffer, save replay buffer, switch scene (scene name as parameter)
- [x] Connection (address, port, password) and rules saved in the config (`obs`); changes apply at once
- [x] "Test connection" shows the result; a failing rule shows its error on the Plugins page and Overview, with an optional Windows notification
- [x] Starting what already runs, or stopping what is stopped, is not an error; saving a replay buffer that is not running is
- [x] Covered by `ObsSinkTests` against a fake OBS WebSocket server
- [ ] Checked against a real OBS by the Player

## Comments

- Connects per Game Event (they are seconds apart), so there is no connection to keep alive. A kill's `save_replay` therefore reaches OBS about 0.5 s after the kill plus the connect time (a few ms locally).
- The OBS password is stored in the config as typed, as OBS stores it in its own settings.
- The audio plugin from the template is not built; only OBS is listed.
- The ideas for later are built in ticket 15.
