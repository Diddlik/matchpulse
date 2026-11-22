# 05: Live capture

**What to build:** While Battlefield 6 runs, MatchPulse watches the game's monitor live and feeds the same Match Detector as the replay, so Game Events arrive while playing. When the game closes, watching stops. If a Match was running, `match_ended` is sent with `result: unknown` and `reason: game_exited`.

**Blocked by:** 02

**Status:** ready-for-agent

- [x] MatchPulse notices `bf6.exe` starting and exiting and finds the monitor it runs on
- [x] DXGI Desktop Duplication captures at 5–10 checks per second, cropping only the Cue regions
- [x] Nothing is captured while `bf6.exe` is not running
- [x] A game exit during a Match sends `match_ended` with `reason: game_exited`
- [ ] Measured in-game: no noticeable frame rate drop with capture running (result written down)
- [x] MatchPulse never reads from or injects into the game process

## Comments

**Ticket 05 code done; in-game checks open (status stays `ready-for-agent` until the Player has run them):**

- Run live mode with `MatchPulse live [--config <path>]`. Replay mode is unchanged.
- `GameWatcher.Run` polls for `bf6.exe` every 2 s by process enumeration, finds the monitor of its main window (`MonitorFromWindow`), and runs `LiveFrameSource` on it. While capturing it checks once per second that `bf6.exe` still runs. It never opens a handle to the game process. A new `MatchDetector` is made for each game run.
- `LiveFrameSource` uses DXGI Desktop Duplication on the output for that monitor. It copies only the Cue regions (every static `Cue` in `Cue.cs`) into a staging texture and hands the Match Detector full-size BGR frames with everything else black. On `AccessLost` (for example a display mode change) it opens the duplication again. If capture fails, the error is logged and capture is retried after 2 s.
- `MatchDetector.GameExited(at)` emits `match_ended` with `result: unknown` and `reason: game_exited` if a Match is running. Until ticket 04, a Match runs from the first frame, so a game exit before any frame yields nothing. Ticket 04 should keep this method and base it on its Match state.
- Checked on the desktop without the game: a copy of `charmap.exe` named `bf6.exe` was detected on start and exit, and the exit sent `match_ended` / `game_exited`. A probe read 30 frames at about 10 per second from a 5120x2160 monitor with only the Kill Badge region filled.
- Not checked: capture while BF6 is in its real display mode, and the frame rate impact.

**Manual verification for the Player:**

1. Put a webhook receiver on, for example, `http://localhost:8080/bf6` and set it in `%APPDATA%\MatchPulse\config.json` with `"checksPerSecond": 10`.
2. `dotnet run --project src/MatchPulse -c Release -- live` in a terminal before starting BF6. The log says `Waiting for bf6.exe`.
3. Start BF6. Within about 2 s of the game window appearing, the log says `bf6.exe started on monitor ...`. No `Capture ... failed` warnings should repeat.
4. Play a REDSEC Match in the game's usual display mode (also try exclusive fullscreen, if used) and get a kill. The log and webhook show a `kill` Game Event within about 1 s.
5. FPS: in the same spot (for example the training range or the deploy screen), note the average FPS for 60 s with MatchPulse stopped, then for 60 s with `live` running (BF6 performance overlay or PresentMon). Write both numbers here. A drop above about 2 % counts as noticeable.
6. Quit BF6 from the menu while a Match is running. The log says `bf6.exe exited: watching stopped` and the webhook gets `match_ended` with `"result":"unknown","reason":"game_exited"`. After that, MatchPulse's CPU use in Task Manager drops to about 0 %.
7. Tick the in-game criterion and set the status to done when 3 to 6 work.

