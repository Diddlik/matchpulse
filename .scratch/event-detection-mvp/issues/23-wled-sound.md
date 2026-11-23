# 23: WLED and sound plugins

**What to build:** Lights and sounds react to Game Events: WLED LED strips flash or switch presets, and MatchPulse plays a tone or a sound file.

**Blocked by:** 14

**Status:** resolved

- [x] WLED: controller address and test; rules "when event → flash colour (solid/blink/strobe) for N s / load preset / set colour / on / off"; a flash restores the previous state, and back-to-back flashes restore the state from before the first
- [x] Sound: rules "when event → built-in sound (ding, double, low, fanfare) or WAV/MP3 file, at a volume"; plays in the background on the default output device
- [x] ▶ per rule runs it as a test; failures show on the Plugins page with an optional notification
- [x] Covered by `WledSoundTests` (in-memory WLED controller, recording player, rendered tones); real playback checked once on the developer PC

## Comments

- WLED uses its JSON API (`/json/state`, `/json/info`) over HTTP, no extra library. Built-in sounds are synthesised (bell-like envelope), so no audio files are shipped; files play through NAudio (MIT).
- The preview's "audio control" (ducking other apps) from the UI template is still not built and was removed from the demo data.
