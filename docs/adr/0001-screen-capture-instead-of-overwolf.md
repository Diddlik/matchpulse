# Detect game events by screen capture, not Overwolf

Overwolf's Battlefield 6 game events would have been the obvious source. We dropped Overwolf because private use relies on dev-mode access that Overwolf can revoke at any time, and public release requires Overwolf monetization. MatchPulse instead captures the screen with a documented Windows capture API and recognises fixed HUD cues by template matching, at 5–10 checks per second. This never touches the game process, so it is safe with anti-cheat. The cost: cues depend on resolution, HUD layout and game language, and every game patch can break them.

## Considered Options

- **Overwolf GEP:** rejected for the dev-mode and monetization dependency above. It also reports rounds rather than matches and gives no game mode.
- **Game log files:** rejected. BF6 writes no usable match log, only crash dumps.
- **OCR:** rejected as the primary method because it is slower and less reliable than template matching on fixed HUD regions.
