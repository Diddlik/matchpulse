# POC 01: Sample Recordings, Cues and fixtures

Ticket: `.scratch/event-detection-mvp/issues/01-poc-samples-cues-fixtures.md`

## Method

All Sample Recordings in `samples/` were decoded with ffmpeg and checked at 5 checks per second with OpenCV template matching (the same method the Match Detector will use). For every check the script recorded the score of each candidate Cue. I then looked at the interesting moments frame by frame at 10 frames per second at full resolution. Short clips around the moments were cut into `fixtures/clips/`, and every clip was checked again at 10 checks per second to get the offsets in its `.expected.json`.

All recordings are 2560×1440 at 60 fps, H.264, game language German, Player name `BulletWaltz`.

## Coverage per sample

Kills are the Player's confirmed-kills counter read from the counter bar near the end of the Match. It includes kills credited to the Player after a Squad member finishes the Player's knockdown (see Surprises). "Deaths" counts spectating periods ("BEOBACHTE"), so a death that ended in a redeploy without spectating may be missing. "HUD gaps" are periods of 20 s or more without the in-match HUD inside a Match: a Jammer, a redeploy screen or a menu. A Jammer is marked confirmed only where I saw it. Times are mm:ss in the recording.

| Sample | Length | Matches | Kills (counter) | Deaths / spectating | Result | Jammer / HUD gaps | Notes |
|---|---|---|---|---|---|---|---|
| 2026-11-05 21-39-33 | 4:49 | 1 (joined in progress) | 4 | 2 | defeat (~4:28) | none | defeat emblem scores only 0.76 in grayscale, 0.92 with the colour mask |
| 2026-11-05 22-16-27 | 11:13 | 1 | 1 | 2 | defeat (11:10) | none | was still copying during the first pass |
| 2026-11-05 22-36-16 | 11:23 | 1 | 6 | 2 | not recorded | none | knock-then-finish and finish-downed clips |
| 2026-11-06 20-58-57 | 42:35 | 3 | 3, 0, 5 | 8 periods | 1st: defeat (~10:05, "5. PLATZ"); 2nd and 3rd: not recorded | several | long session with menus, lobby and Match intro |
| 2026-11-06 22-53-38 | 19:35 | 1 | 2 | 1 (18:27 to end) | not recorded | none | Match start clip, death-spectate clip |
| 2026-11-08 21-57-29 | 12:45 | 1 | 8 | 0 | victory (11:50) | none | victory, kill and Squad clips |
| 2026-11-08 22-56-32 | 12:25 | 1 | 6 | 1 | victory (11:55) | none | |
| 2026-11-08 23-11-06 | 11:54 | 1 | 3 | 2 | defeat (11:45) | none | defeat clip |
| 2026-11-12 21-16-56 | 26:04 | 1 | n/a | n/a | n/a | n/a | **not REDSEC**: a ticket-based mode with a different HUD. No REDSEC Cue matches at all. |
| 2026-11-14 12-11-37 | 11:56 | 1 | 2 | 0 | victory (11:27) | Jammer 10:52 to 11:23 (confirmed) | Jammer clip |
| 2026-11-15 21-46-18 | 8:28 | 1 | 5 | 0 | defeat (~8:04) | gap 5:32 to 6:04 | |
| 2026-11-15 22-31-05 | 10:37 | 1 | 4 | 0 | not recorded | gap 2:49 to 3:51 (minimap noise, likely Jammer) | |
| 2026-11-15 22-45-34 | 11:20 | 1 | 4 | 3 | defeat (11:20) | none | |
| 2026-11-17 22-26-17 | 10:48 | 1 | 4 | 1 (9:16 to end) | not recorded | none | |
| 2026-11-18 21-43-21 | 8:41 | 1 | 4 | 3 | defeat (8:32) | gap 5:27 to 5:48 | |
| 2026-11-18 23-31-48 | 9:48 | 1 | 3 | 1 | defeat (~9:43) | gap 8:17 to 8:58 | defeat emblem 0.50 in grayscale, 0.92 with colour mask |
| 2026-11-19 20-47-23 | 19:39 | 1 | 4 | 1 (at end) | not recorded | gap 1:23 to 1:55 | |
| 2026-11-19 21-10-29 | 11:02 | 1 | 1 | 1 (at end) | not recorded | none | |
| 2026-11-19 21-25-08 | 5:43 | 1 | 2 | 1 | not recorded | none | |
| 2026-11-19 22-29-57 | 8:55 | 1 | 2 | 2 | defeat (8:54) | gap 7:06 to 7:54 | Kill Marker at 7:37 inside the gap; not confirmed as a Jammer kill |
| 2026-11-19 22-42-06 | 8:52 | 1 | 3 | 1 | defeat (8:31) | none | |
| 2026-11-19 22-53-02 | 11:07 | 1 | 2 | 1 (at end) | not recorded | gap 2:10 to 2:54 | |
| 2026-11-19 23-07-00 | 11:13 | 1 | 7 | 4 | not recorded | gap 7:04 to 7:37 | |
| 2026-11-19 23-21-32 | 9:48 | 1 | 4 | 1 | defeat (~9:41) | gap 8:06 to 9:08 | |

Totals: 24 recordings (one not REDSEC), 25 REDSEC Matches, 3 victories, 12 defeats. The rest end before the result, because the recording stops while spectating or before the end screen. All are German. There is no English footage. There is one confirmed Jammer and several likely ones, but no confirmed Kill during a Jammer.

## Chosen Cues

Regions are fractions of the frame (x, y, w, h). "Template" is where the template was cut. "Search region" is where it must be searched, because some Cues move. Templates are in `fixtures/cues/`, cut from 2560×1440 frames.

| Cue | Template file | Template (x, y, w, h) | Search region (x, y, w, h) | Language | Notes |
|---|---|---|---|---|---|
| In-match HUD (Match start, Match running) | `in_match_hud.png` (squads icon with left bracket of the counter bar) | 0.8555, 0.0556, 0.0211, 0.0306 | 0.742, 0.042, 0.258, 0.059 | neutral | The counter bar is right-aligned and grows from 2 to 4 counters, so the icon moves left by up to about 0.14 of the width. Grayscale TM_CCOEFF_NORMED: 0.95–1.0 normally, 0.75–0.8 on bright fire backgrounds and while spectating, below 0.5 elsewhere. Use a threshold of about 0.7. |
| End of Match, victory | `end_victory.png` (gold emblem with laurels above "SIEG") | 0.4336, 0.2188, 0.1328, 0.1806 | 0.4219, 0.1979, 0.1563, 0.2222 | neutral | Animates in for about 1.5 s, then stays still for 30 s or more. Score 0.97–1.0 when settled. Matching at half resolution works. |
| End of Match, defeat | `end_defeat.png` (red shield emblem above "SQUAD ELIMINIERT") | 0.4523, 0.1819, 0.0953, 0.2208 | 0.4375, 0.1528, 0.1250, 0.2778 | neutral | Visible only about 2 s (score 0.83–0.99, single dropped checks happen). K must stay small: 3 at 5 checks/s. |
| Kill Marker | `kill_marker.png` and `kill_marker_mask.png` (red X around the crosshair) | 0.4609, 0.4306, 0.0781, 0.1389 | 0.4531, 0.4167, 0.0938, 0.1667 | neutral | Match the mask against a red HSV mask of the frame (H ≤ 8 or ≥ 172, S > 120, V > 170), not grayscale. Visible only 0.2–0.4 s and grows outward while visible. Score 0.6–0.77 on a hit, weaker marks 0.4. Also shown on a knockdown, see below. |
| Player's Kill Feed entry | `kill_feed_player_marker.png` (green triangle `▸` in front of the Player's name) | 0.7332, 0.1694, 0.0070, 0.0125 | 0.6836, 0.0903, 0.3164, 0.2431 | neutral | The Player's name is not pink. It is green (HSV about H 67, S 133, V 207) with a `▸` in front. Squad names are a paler lime green (H 49, S 87, V 250), enemies are salmon red. The marker is tiny, so it must be combined with the colour test and the row position. The entry type is in the icon between the names: weapon = kill, double chevron = knockdown, skull = finished a downed opponent, X = died (bleed-out or squad wipe). |
| End emblems, colour masks | `end_victory_mask.png`, `end_defeat_mask.png` | as the emblems | as the emblems | neutral | Binary masks of the gold (H 20-36) and red (H <= 18 or >= 170) emblem pixels. Match them against the same colour mask of the frame. Recommended over grayscale, see Surprises. |
| Kill counter flash (recommended addition) | `kill_counter_flash.png` (skull with tick, green while flashing) | 0.9430, 0.0569, 0.0164, 0.0278 | same band as the in-match HUD | neutral | The personal "confirmed kills" counter in the counter bar turns green with a green `▲` for about 0.6 s whenever a kill is credited to the Player. Locate the skull by grayscale shape (score > 0.7), then require most of its bright pixels to be green (H 35–85, S > 50). Clean on 5 of 6 test clips. It fails on bright orange backgrounds where the translucent HUD washes out. |
| Spectating (coverage only) | `spectate_label_de.png` ("BEOBACHTE") | 0.4297, 0.7403, 0.1406, 0.0431 | 0.414, 0.722, 0.172, 0.083 | German only | Not a Game Event. Used to measure deaths. |

## Answers to the open questions

**Does the Kill Marker appear only when the Player finishes an opponent, or also on a knockdown?** Also on a knockdown. The red X looks the same and animates the same way on a knockdown, on finishing a downed opponent and on an outright kill (`kill_knock-then-finish_1105-2236` shows both within 0.8 s; `knock_squad-finishes_1108-2157` shows a knockdown that the Player never finishes). A Kill Marker alone therefore over-counts Kills.

**Is each Cue language-neutral, or does it need a template per language?** All chosen Cues are icons, shapes or colours and should be language-neutral: the counter bar icons, both end emblems, the Kill Marker, the kill counter skull and the `▸` marker. The texts next to them ("SIEG", "SQUAD ELIMINIERT", "BEOBACHTE", "KILL BESTÄTIGT") would need a template per language, so they were not chosen. This is not verified on English footage yet, because there is none.

## Missing recordings for the Player

1. English game language: at least one full Match with a victory or a defeat, several Kills and a death with spectating. Needed for ticket 07 and to confirm that the emblems and the counter bar are the same in English.
2. A Kill while a Jammer is active (the Player kills an opponent while "Störsender in der Nähe" is shown). The samples contain three Jammer periods, but no Kill during them.
3. A second 16:9 resolution, for example 1920×1080, with a few Kills and a Match end. Needed for ticket 07.
4. A game exit in the middle of a Match (close the game while in a Match). Needed for ticket 05.
5. Nice to have: a defeat where the Player is still alive until the squad is wiped, and a Match that is left early through the menu, to see which screens appear.

Victory is covered (3 victories in the samples), so no extra victory recording is needed.

## Surprises that affect tickets 02–08

1. **The Kill Marker also shows on a knockdown** (see the answer above). Ticket 02 must not emit a `kill` from the Kill Marker alone. Recommended rule for 02 and 03: emit `kill` when the kill counter flash Cue fires, because it only fires when the counter goes up. As an alternative, emit it when a Kill Marker is followed within about 1 s by a Player Kill Feed entry whose icon is a weapon or a skull, not a chevron. A Jammer hides both the Kill Feed and the counter bar, so the spec rule "with a Jammer the Kill Marker alone counts" would also count knockdowns. The Player needs to decide this.
2. **The game credits the Player with kills he did not finish.** When the Player knocks an opponent and a Squad member finishes him, or the opponent dies in a squad wipe, the Player gets "KILL BESTÄTIGT" and his kill counter goes up (`knock_squad-finishes`, `squad-kill-wipes-players-knock`). By the glossary these are not Kills, so the fixtures expect no `kill` there. The Player should confirm this. If he wants them counted, the kill counter flash becomes the only Kill Cue, which is simpler.
3. **The Player's name in the Kill Feed is green with a `▸`, not pink.** Pink appears only on the Player's badge in the squad list. Squad names are a paler green, so the colour test must be tight.
4. **A Jammer hides the whole counter bar**, and that bar is also the in-match HUD Cue. Jammers last 30–60 s. Ticket 04's `hud_lost` timeout must be longer than that (I suggest 120 s), or the Jammer panel must count as "in match". The minimap also shows noise ("KEIN SIGNAL"), which could serve as a Jammer Cue.
5. **The in-match HUD Cue moves.** The counter bar is right-aligned and holds 2 to 4 counters (helicopter, squads, players, kills, spectators). Cue regions in tickets 02 and 07 need a search band, not a fixed spot. The counter bar stays visible while spectating (without the kill counter), which fits the rule that death does not end the Match.
6. **The defeat screen is short and transparent.** The red emblem shows for about 2 s over the live scene, and grayscale matching scores only 0.5–0.76 on some samples. A colour mask of the emblem (`end_defeat_mask.png`, `end_victory_mask.png`) scored 0.89–1.0 on every end screen I checked and 0.0 on gameplay. Use colour masks for both end Cues.
7. **Not every recording ends with a result.** 10 of 25 Matches end before the end screen, because the Player leaves while spectating. The live app will see the same when the Player quits to the menu, so `hud_lost` with `result: unknown` will be common, not rare.
8. **Other game modes:** `2026-11-12 21-16-56` is not REDSEC. None of the Cues fire there, which is correct for the MVP. Ticket 04 should not expect Matches from other modes.
9. **The Kill Marker is short:** 0.2–0.4 s. At 5 checks per second it is seen on one or two checks. Ticket 02 should use 10 checks per second and K = 1 for the Kill Marker, with the confirmation step from item 1 guarding against flicker.
10. The fixture clips are re-encoded at 30 fps, so the replay Frame Source must sample by timestamp, not by frame count.

## Fixtures

Clips are in `fixtures/clips/`, each with a sibling `.expected.json` (source recording and start time, game language, expected Game Events with offsets in seconds from clip start, notes).

The clips are re-encoded, not cut with `-c copy`. The source is 40 Mbit/s at 60 fps (about 5 MB per second), so a lossless cut of the needed seconds would have been far over the size budget, and keyframes are 2.5–4 s apart. The clips are libx264 CRF 26, 30 fps, 2560×1440, without audio. All Cues were re-checked on the re-encoded clips and still match.

| Clip | Source (start s) | Expected Game Events (offset s) | Covers |
|---|---|---|---|
| `kill_direct_1108-2157` | 2026-11-08 21-57-29 (511) | match_started 0.0, kill 1.7 | Player Kill, outright |
| `kill_finish-downed_1105-2236` | 2026-11-05 22-36-16 (132.5) | match_started 0.0, kill 1.9 | Player finishes a downed opponent |
| `kill_knock-then-finish_1105-2236` | 2026-11-05 22-36-16 (232) | match_started 0.0, kill 2.0 | knockdown Kill Marker, then the finishing Kill Marker; one kill |
| `knock_squad-finishes_1108-2157` | 2026-11-08 21-57-29 (88) | match_started 0.0 | Player knocks, Squad finishes; no kill |
| `squad-kill-wipes-players-knock_1108-2157` | 2026-11-08 21-57-29 (103.5) | match_started 0.0 | Squad kill; no kill |
| `match-start_1106-2253` | 2026-11-06 22-53-38 (15) | match_started 6.2 | Match start after the helicopter intro |
| `victory_1108-2157` | 2026-11-08 21-57-29 (707) | match_started 0.0, match_ended 3.3 victory | victory |
| `defeat_1108-2311` | 2026-11-08 23-11-06 (702.5) | match_started 0.0, match_ended 2.5 defeat | defeat |
| `death-spectate_1106-2253` | 2026-11-06 22-53-38 (1098) | match_started 0.0 | death followed by spectating; the Match does not end |
| `jammer-start_1114-1211` | 2026-11-14 12-11-37 (649) | match_started 0.0 | a Jammer starts, HUD and Kill Feed vanish; the Match does not end |

The clips total about 96 MB.

`match_started` at offset 0.0 means the in-match HUD is already visible on the first frame, so a fresh Match Detector starts the Match there. `match_ended` uses `reason: end_screen` for an end recognised from the end-of-match screen. The spec only names the fallback reasons, so ticket 04 should confirm this value.
