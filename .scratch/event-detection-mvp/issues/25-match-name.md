# 25: Match Name from the lobby, with a list the Player corrects

**What to build:** `match_started` carries the Match Name: the mode and the map the lobby shows top left before a Match, read with Windows OCR. Names read for the first time are added to a list in Settings, where the Player corrects misreadings; later reads nearly the same as a listed name are replaced by it.

**Blocked by:** 04

**Status:** resolved

- [x] While no Match runs, the lobby header (top left, `MatchNames.Region`) is read every 2 s with `Windows.Media.Ocr` in the game language; the line "map: area" and the line above it (the mode) make one read
- [x] At `match_started`, the reads of the last 5 minutes agree on one name (the one read most often, then the one most like the others); `data.mode` and `data.map` carry it
- [x] Each is replaced by a name from `matchNames` (config) with a similarity of at least 0.9 (edit distance on letters and digits); otherwise it is new and added to `matchNames`
- [x] Settings, section "Match names": the list, one per line, editable; OBS placeholders `{mode}` and `{map}`
- [x] No OCR for the game language installed in Windows: logged, `match_started` without name
- [x] Covered by `MatchNamesTests` (lobby pictures in `fixtures/names/`), `UiHubTests`, `ConfigTests`, `ObsSinkTests`; recording 2026-11-25 22-28-19 replayed: 28 reads, single ones misread ("FL SSUFER", "#LUSSUFEN"), agreed name "RAPID ROYALE - TRIOS / MIRAK-TAL: FLUSSUFER"

## Comments

Match boundaries exist only in REDSEC (ticket 10 is open), so multiplayer lobbies are read but no event carries their name yet.
