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
The Player finishing off an opponent.
_Avoid_: elimination, frag

**Cue**:
A visual element on screen whose presence tells MatchPulse that something happened or that the game is in a certain state.
_Avoid_: marker, template, indicator

**Kill Feed**:
The in-game list of recent kills shown on the HUD. The Player's name is highlighted there, so the Player's own Kills can be told apart from the Squad's.

**Kill Marker**:
The skull symbol in the centre of the screen that confirms the player's kill. It is still shown when the Kill Feed is hidden by a Jammer.
_Avoid_: hit marker, kill icon

**Jammer**:
An in-game effect that hides parts of the HUD, including the Kill Feed.
_Avoid_: Störsender (in code and docs)
