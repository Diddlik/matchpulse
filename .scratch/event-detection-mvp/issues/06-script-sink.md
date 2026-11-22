# 06: Script sink

**What to build:** The Player can configure scripts that run for every Game Event and receive the event JSON on stdin, so OBS or any other tool can be driven without MatchPulse knowing about it.

**Blocked by:** 02

**Status:** resolved

- [x] Scripts are configured in the config file next to webhooks
- [x] Each Game Event starts each configured script once, with the event JSON on stdin
- [x] A failing, hanging or slow script or webhook neither blocks detection nor delays other sinks, and the failure is logged

## Comments

**Ticket 06 done:**

- `ScriptSink` (`src/MatchPulse/ScriptSink.cs`) implements `IEventSink`. Config: `"scripts": ["pwsh -NoProfile -File C:\obs.ps1"]` next to `"webhooks"` (optional, defaults to none). Each entry is a command line run through `cmd.exe /c`, once per Game Event, with the event JSON on stdin and stdin then closed.
- A non-zero exit code throws; a script still running after 10 s has its process tree killed and throws `TimeoutException`. The Event Dispatcher logs both. A script that exits without reading stdin is not a failure by itself; its exit code decides.
- No dispatcher change: each sink already has its own queue and worker, so a hanging sink only delays its own later events (bounded by the 10 s timeout, webhooks by the 10 s `HttpClient` timeout).
- Manual run: config with webhook `http://127.0.0.1:9/dead` and scripts `pwsh -NoProfile -File record.ps1` (appends stdin to a file), `exit 3` and `ping -n 60 127.0.0.1 >nul`; replayed `squad-kill-wipes-players-knock_1108-2157.mkv`. At 21:05:44.017 the `kill` was dispatched; 44.088 `exit 3` logged "exited with code 3"; 44.415 `record.ps1` wrote the exact event JSON; 46.106 the webhook failure was logged; 54.114 the `ping` script was logged as killed after 10 s. Detection and the other sinks were not held up by the hanging script.
