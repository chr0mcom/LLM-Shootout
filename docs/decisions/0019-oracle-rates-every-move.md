# 0019 - An oracle rates every move

Status: accepted (2026-11-07)

## Context
The series focuses on game strength and rule compliance. Strength is measured against a reference: for a game that can be solved, the
position before every move is won, lost or drawn for the player to move, and a move either keeps that value or gives it up. The arena
has to ask the game for this value, write it down next to the move, and hand it to the reports, without letting any player see it.
An earlier plan let the game rate moves inside `Apply`. That puts a second job into every game (rate the move *and* play it) and
rates a move only after it has been applied, when the position before it is gone.

## Decision
- **A game can say how a seat stands: `IGameOracle`.** The running game implements this interface in addition to `IGame`
  (one file: `Games/IGameOracle.cs`). `Assess(TurnRequest)` returns a `PositionAssessment(PositionValue Value, IReadOnlyList<string> BestMoves)`
  for the seat that is to move, before it moves, assuming that both sides play by the rules from here on. `PositionValue` is `Unknown`, `Won`,
  `Lost` or `Drawn`. `BestMoves` lists all regular moves that keep the value, normalized like the moves of `Check`, in a fixed order; it is
  empty in a lost or unknown position. `ChooseMove(turn, assessment, rng)` serves the player type `optimal`.
- **The arena rates, the game only knows the position.** `MatchRunner.PlayRoundAsync` asks `Assess` once for every pending turn of the round,
  before any answer is collected, if `Game is IGameOracle`. After the turns of the round have been recorded and before `Apply`, it writes one
  fact `oracle` for every turn with the status `Played` and a move. The fact has the audience `Analysis` and the acting seat. A turn that
  ended `exhausted` has no move and no fact. A game without an oracle writes no such fact.
- **The data of the fact:** `value` (`won`, `lost`, `drawn`, `unknown`), `move`, `best` (the best moves separated by commas, at most 20),
  `optimal` (`true` or `false` if the move is a regular move in a won or drawn position, otherwise `n/a`), `rated` (`false` for a resignation
  and a rule violation, which are neither a good nor a bad regular move) and `turn`. The text comes from the arena template `fact.oracle`
  (German and English), because the fact is the same for every game.
- **No player ever sees it.** `Timeline.VisibleTo` drops facts of the audience `Analysis`, and the debriefing builds its history through
  the same method. A test plays a whole match with a debriefing and looks for the wording of the fact in every prompt.
- **The fact `rule_violation` names the position.** If the game has an oracle, the fact of a rule violation carries `value`, the position
  before the move, so that a report can tell a violation in a lost position from one in a won position.
- **Reading it back:** `MatchRecord.Verdicts(seat)` returns `MoveVerdict`s: position, move, best moves, `Optimal`, `Rated`, and, joined over turn
  id and seat from the `turn` event, the kind of move and the estimate. `MoveVerdict.Squandered` means a regular move in a won position that
  was not one of the best moves. A protocol without `oracle` facts (an older one, or a game without an oracle) has no verdicts, and every figure
  that is computed from them is "not applicable", never 0.
- **One class computes the figures.** `PlayMetrics.For(match, seat)` adds to its counts: regular moves in a won position, the optimal ones among
  them, the won positions given up (a regular move in a won position that is not a best move: the mistakes), the first mistake, and the mistake
  rate as a `Fraction`; the mean estimate in won and in lost positions and the Brier score (the mean of `(estimate / 100 - y)²` with `y` 1 for won,
  0 for lost, 0.5 for drawn; unknown positions are left out). A resignation, a rule violation and an exhausted turn are never mistakes. A figure
  whose denominator is 0 is not applicable (`null`), never 0, and a protocol without `oracle` facts has no figures at all. `match.json`, the report
  sections "Play strength" and "Calibration" and the sheet of the Excel export read from it.
- **A test game proves the interface.** `TakeAwayGame` (one heap, take 1 to 3, the last stone wins) implements it: the position is lost exactly
  when the heap is a multiple of 4. An option lets it take other numbers, so that a position can have several best moves.

## Consequences
- A game that wants to be rated implements one interface with two methods. The rules of the game stay in `Check` and `Apply`.
- `Assess` runs once per turn and must be fast enough for that; a game with a large search space memoizes inside the game.
- The oracle states what is best under correct play. A resignation or a forbidden action is never compared with it, which keeps both out of
  the measure of strength; they are counted as rule compliance.
- A simultaneous round assesses both seats before either has answered, from the same state; the oracle has to handle that case itself
  (a value of `Unknown` is always allowed).
- The fact adds one line per move to the report history and one event per move to the protocol.
