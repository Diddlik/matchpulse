# 07: Resolution scaling and game language DE/EN

**What to build:** Detection works without calibration at any 16:9 resolution with the default HUD scale, and in German and English. Cue regions are relative to the frame, and templates authored at 2560×1440 are scaled to the actual size. Text Cues have one template per language, chosen by a game language setting. A new language needs only new templates, no code.

**Blocked by:** 02

**Status:** ready-for-agent

Source material: the two English REDSEC MP4 recordings at 1920×1080 in `samples/` (third-party gameplay, a different player). Verify the Cues on them, then cut English 1080p fixture clips.

- [x] Fixture test: the same Game Events are detected at a second 16:9 resolution
- [ ] Fixture test: English footage yields the expected Game Events with the game language set to EN
- [x] A game language setting in the config file, with DE and EN supported
- [x] Language-neutral Cues use one template for all languages

## Comments

**Ticket 07 work (criterion 2 stays open, so the status stays `ready-for-agent`):**

- `Cue.LoadTemplate(frameSize, language)` scales every template from 2560x1440 by frame height / 1440 (INTER_AREA). It reads `cues/<language>/<template>` if that file exists, else `cues/<template>`, so a text Cue for a new language is just a new file. `MatchDetector` loads its templates on the first frame and again if the frame size changes. Ticket 04: load `in_match_hud` and the end masks the same way in `LoadTemplates`.
- Game language: `new MatchDetector(gameLanguage: ...)`. `GameWatcher.Run` takes `Func<string> gameLanguage` and reads it each time `bf6.exe` starts. The tray passes `() => Config.GameLanguage`, so a changed setting applies from the next game run without restarting MatchPulse. Replay and `live` use the config value.
- All Cues are language-neutral on the English footage. The end emblems, the counter bar, the Kill Badge and the Knockdown Badge look the same; only the texts differ ("VICTORY", "SQUAD ELIMINATED", "SPECTATING", "+50"). So there are no per-language templates, and the setting is a hook with no effect today. The fixture tests pass each clip's `gameLanguage` from its `.expected.json`.
- The scale is right: on the 1080p footage the in-match HUD and the kill counter score highest at 0.75 of the reference size. The Kill Badge scores 0.83 to 0.94 and the Knockdown Badge 0.92 to 0.97.
- Fixtures: `Clip_at_1920x1080_yields_the_same_kills` replays every German kill clip downscaled to 1920x1080 and expects the same kills. There are 5 new English 1080p clips (`en_*`, 22 MB). `en_squad-credit_redsec-en2` (a Kill Badge without a Kill Marker, no kill) is in the kill test. `en_match-start_redsec-en2`, `en_victory_redsec-en1` and `en_defeat_redsec-en2` need ticket 04 before they can be tested.
- **Open, blocks criterion 2:** `en_knock-then-kill_redsec-en2` expects a kill but gets none. In both English videos the Kill Marker never reaches 0.3. Its red X is dim (HSV V 110 to 140, but the mask needs more than 170) and thin. It may also be smaller while aiming down sights (about half the template size at 282 s). The likely cause is the YouTube re-encode with 4:2:0 chroma, which smears the 2 px red lines at 1080p. Lossless live capture may not have this problem. The kill counter flash also fails on this footage, because the green is pale. Loosening the colour bands and adding half-size matching gave noise of 0.3 to 0.34 on `death-spectate`, so I did not change the Kill Marker. To settle this, the Player should record a few kills at 1920x1080 with the game in English.
- Differences from the German samples: console gameplay (PS button prompts), a YouTube "Subscribe" overlay at the bottom right in the first seconds, the video cuts straight from one Match's "7TH PLACE" screen to the next Match's intro (`en2` at about 1100 s), and the victory emblem stays only about 3 s before a "WINNERS" outro.
