# 15: More OBS actions

**What to build:** The OBS plugin covers what a Player wants around kills and Matches: chapter markers, one file per Match, a delayed replay save, a kill counter text, a kill overlay, named replays, streaming and pausing.

**Blocked by:** 14

**Status:** ready-for-human

- [x] New actions: pause/resume recording, split recording (`SplitRecordFile`), chapter marker (`CreateRecordChapter`, OBS 30.2+ with Hybrid MP4), set text of a source, show a source for N seconds, start/stop streaming
- [x] Every rule can run after a delay (0-60 s) without holding up the next Game Event; its result shows like any other
- [x] Save replay can rename the saved file from a template (only when OBS runs on this PC)
- [x] Placeholders in texts and names: `{kills}`, `{match}`, `{n}`, `{result}`, `{date}`, `{time}`
- [x] Covered by `ObsSinkTests` against the fake OBS WebSocket server
- [ ] Checked against a real OBS by the Player

## Comments

- `{kills}` is computed from the Game Event (in a Match, `match_started` is sequence 1 and every later event but `match_ended` is a kill), so it needs no state and survives saving settings.
- Show source: the source is looked up in the current program scene; hiding it again runs on its own connection after the duration.
- Renaming waits for OBS's `ReplayBufferSaved` event (up to 15 s), then renames next to the original; a taken name gets `-2`, `-3`, …
