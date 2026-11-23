# MatchPulse

MatchPulse watches Battlefield 6 on screen and turns what happens into events: a Match starts, the Player gets a Kill, a Match ends with victory or defeat. It sends each event to webhooks, scripts and OBS, so recordings, clips, overlays or statistics can follow the game by themselves.

MatchPulse only reads the screen, like recording software. It never reads from or writes to the game process.

- **Kills** in every mode, also under a Jammer, without counting kills finished by the Squad
- **Matches** in REDSEC (battle royale): start, victory, defeat, the Player's knockdowns and death
- **Targets:** webhooks (HTTP POST with the event JSON) and scripts (event JSON on stdin)
- **OBS plugin** over OBS WebSocket: recording, replay buffer, chapter markers, scenes, texts, overlays, streaming
- **Live monitor:** a picture of every kill, also in a window of its own for a second screen
- **Diagnostic mode:** pictures of unclear cases, to find missed kills
- German and English, light and dark

## Install

Download `MatchPulse-Setup-<version>.exe` from [Releases](https://github.com/Diddlik/matchpulse/releases) and run it. No admin rights needed. MatchPulse then runs in the tray; double-click the icon to open it. Windows 10 (1809) or 11 with the Edge WebView2 Runtime (part of Windows 11).

In MatchPulse, set the **Kill Marker colour** to the colour of the hit X in your game settings (red by default).

## Event JSON

```json
{ "type": "kill", "matchId": "91fdb6ce…", "sequence": 2, "occurredAt": "2026-11-23T20:18:40+01:00", "data": {} }
```

`type` is `match_started`, `kill` (`data.kills`: the Player's kills in the Match so far), `knockdown`, `death` or `match_ended` (`data.result`: `victory`, `defeat`, `unknown`; `data.reason`: `end_screen`, `hud_lost`, `game_exited`, `stopped`; `data.kills`). Knockdowns and deaths are reported in REDSEC; deaths for the German game language so far. Kills outside a REDSEC Match have `matchId: null`. Test events from the app have `type: "test"`.

## Build

.NET SDK 11, Windows.

```powershell
dotnet test
dotnet run --project src/MatchPulse -- --show                 # tray app with the window open
dotnet run --project src/MatchPulse -- live                   # detection in the terminal
dotnet run --project src/MatchPulse -- replay <video.mkv>     # detection on a recording
```

The fixture clips (`fixtures/clips/*.mkv`) are cut from private recordings and are not in this repository; only their `.expected.json` files are. Without them, run `dotnet test --filter "FullyQualifiedName!~FixtureClipTests"`.

A release is built by pushing a tag `v<version>`: GitHub Actions tests, publishes, builds the installer (Inno Setup, `installer/MatchPulse.iss`) and a portable zip, and attaches them to the release.

MIT licence, see `LICENSE`. Battlefield is a trademark of Electronic Arts; MatchPulse is not affiliated with EA.

Design notes: `GLOSSARY.md`, `docs/adr/`, `docs/ui-brief.md`, `.scratch/event-detection-mvp/`.
