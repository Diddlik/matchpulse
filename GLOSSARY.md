# MatchPulse

MatchPulse watches the screen while Battlefield 6 runs, recognises what happens in the game, and announces it as game events for other tools to act on.

## Language

**Game Event**:
A fact about the game that MatchPulse announces, such as a match starting or a kill.
_Avoid_: signal, trigger, notification

**Match**:
One game the player takes part in, from the moment the match start is recognised on screen until its end is recognised.
_Avoid_: round, session, game

**Player**:
The person running MatchPulse. Game Events are only about what the Player does or experiences.
_Avoid_: user, me, self

**Squad**:
The Player's teammates in a Match. Their actions are not reported as the Player's Game Events.
_Avoid_: team, party

**Kill**:
The Player finishing off an opponent, in any game mode. A Kill outside a Match (regular multiplayer) belongs to no Match.
_Avoid_: elimination, frag

**Knockdown**:
The Player putting an opponent into the downed state without killing him. Finishing him later is a Kill of its own.
_Avoid_: down, knock

**Death**:
The Player being eliminated from the Match (spectating the Squad afterwards). Being knocked down alone is not a Death.
_Avoid_: elimination

**Cue**:
A visual element on screen whose presence tells MatchPulse that something happened or that the game is in a certain state.
_Avoid_: marker, template, indicator

**Kill Feed**:
The in-game list of recent kills and knockdowns shown on the HUD. Its colours depend on the Player's game settings.

**Kill Marker**:
An X around the crosshair that appears when the Player knocks down or kills an opponent. It is red by default; the Player sets its colour in the game settings, and MatchPulse has to be told the same colour. It looks the same for both, so on its own it does not prove a Kill. A Kill Marker followed shortly by a Kill Badge does. It is still shown when a Jammer hides the Kill Feed.
_Avoid_: hit marker, kill icon, skull

**Score Popup**:
The row of icons with a point total that appears just below the crosshair while the Player earns points. Each new action adds one icon to the row.
_Avoid_: score display, notification

**Kill Badge**:
The skull icon in the Score Popup that appears when a kill is credited to the Player: with a tick in REDSEC, without one in regular multiplayer. It also appears when the Squad finishes an opponent the Player knocked down, so it proves a Kill only when a Kill Marker came shortly before it.
_Avoid_: skull, kill icon

**Knockdown Badge**:
The skull-with-down-arrow icon in the Score Popup that appears when the Player knocks an opponent down.
_Avoid_: down icon

**Jammer**:
An in-game effect that hides parts of the HUD, including the Kill Feed and the counter bar, for roughly 30 to 60 seconds.
_Avoid_: Störsender (in code and docs)
