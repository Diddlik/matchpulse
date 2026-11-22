# 05: Live capture

**What to build:** While Battlefield 6 runs, MatchPulse watches the game's monitor live and feeds the same Match Detector as the replay, so Game Events arrive while playing. When the game closes, watching stops. If a Match was running, `match_ended` is sent with `result: unknown` and `reason: game_exited`.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] MatchPulse notices `bf6.exe` starting and exiting and finds the monitor it runs on
- [ ] DXGI Desktop Duplication captures at 5–10 checks per second, cropping only the Cue regions
- [ ] Nothing is captured while `bf6.exe` is not running
- [ ] A game exit during a Match sends `match_ended` with `reason: game_exited`
- [ ] Measured in-game: no noticeable frame rate drop with capture running (result written down)
- [ ] MatchPulse never reads from or injects into the game process
