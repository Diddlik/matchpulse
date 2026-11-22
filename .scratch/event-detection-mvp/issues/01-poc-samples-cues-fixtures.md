# 01: POC: evaluate samples, choose Cues, cut fixtures

**What to build:** Work through the Sample Recordings in `samples/` and settle which Cues MatchPulse will use, so detection rests on observed footage instead of assumptions. The result is a written POC report, cut Cue templates, and short fixture clips with an expected Game Event list each. Any recordings the samples lack are handed to the Player as a list to play and record.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] Coverage report: per sample, the Matches, Kills, deaths, results, game language and any Jammer moments it contains
- [ ] List of missing recordings for the Player (at least English game language, a Kill with an active Jammer, and a victory, if not covered)
- [ ] A chosen Cue for each of: in-match HUD (Match start), end-of-match screen (victory and defeat), Kill Marker, and the Player's highlighted Kill Feed entry, each with its screen region relative to the frame
- [ ] Templates cut from the 2560×1440 samples for each chosen Cue
- [ ] Answered: does the Kill Marker appear only when the Player finishes an opponent, or also on a knockdown?
- [ ] Answered: is each Cue language-neutral, or does it need a template per language?
- [ ] Short clips of a few seconds cut losslessly around each relevant moment, each with an expected Game Event list; full samples stay out of version control
