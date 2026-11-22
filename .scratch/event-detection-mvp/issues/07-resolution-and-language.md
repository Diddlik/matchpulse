# 07: Resolution scaling and game language DE/EN

**What to build:** Detection works without calibration at any 16:9 resolution with the default HUD scale, and in German and English. Cue regions are relative to the frame, and templates authored at 2560×1440 are scaled to the actual size. Text Cues have one template per language, chosen by a game language setting. A new language needs only new templates, no code.

**Blocked by:** 02; the English fixtures also need the English recordings requested in 01

**Status:** ready-for-agent

- [ ] Fixture test: the same Game Events are detected at a second 16:9 resolution
- [ ] Fixture test: English footage yields the expected Game Events with the game language set to EN
- [ ] A game language setting in the config file, with DE and EN supported
- [ ] Language-neutral Cues use one template for all languages
