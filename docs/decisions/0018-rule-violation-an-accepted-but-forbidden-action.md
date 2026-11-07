# 0018 - Rule violation: an accepted but forbidden action

Status: accepted (2026-11-07)

Amended 2026-11-07: the fact `rule_violation` also carries `value`, the position before the move, if the game has an oracle ([ADR 0019](0019-oracle-rates-every-move.md)).

## Context
The series focuses on game strength and rule compliance. Until now the arena knew one kind of rule problem: an invalid move. `Check`
rejects it, the player gets a correction hint and tries again, and after the last attempt the turn is `exhausted`. That measures whether a
player can produce a legal answer, but not what a player does when the game would carry out an action that the rules forbid. Such an
action is easy to imagine in many games (passing, taking back a move, looking at a hidden field, taking more than the limit when the program
does not check it). To study it, the game has to offer the action, the arena has to let it happen, and the protocol has to say that it did.

## Decision
- **A move check can accept three kinds of move.** `MoveCheck` gets `MoveKind { Regular, Resignation, RuleViolation }`, a `ViolationCode`,
  and the factories `Ok`, `Resign`, `Violation(move, code)` and `Reject`. `IsValid` means "accepted" for all three. A rejected move
  has no kind.
- **The kind travels with the move.** `Evaluation`, `TurnResult` and `TurnSubmission` (`MoveKind Kind = Regular`) carry it, so `Game.Apply`
  sees it. `AnswerEvaluator` accepts all three without a correction hint and without another attempt. A turn that ends `exhausted`
  has no kind.
- **The protocol records it.** `TurnEvent.MoveKind` is null for a regular move and written as `moveKind` only otherwise, so protocols
  of games without such moves are unchanged. The protocol stays at version 2 (introduced by [ADR 0017](0017-self-assessment-per-turn-and-protocol-version-2.md)
  on the same day, before any protocol of version 2 was published); version 1 protocols read as "regular".
- **The arena writes the fact `rule_violation`.** In `MatchRunner.PlayRoundAsync`, after the turn has been recorded and before `Apply`, the arena
  adds a fact of the kind `rule_violation` with the audience `Analysis`, the acting seat, and the data `code`, `move` and `turn` (the turn id).
  The wording comes from an arena template (`fact.rule_violation`, German and English), because the fact is the same for every game.
  Facts of the audience `Analysis` are never rendered into a prompt or into a debriefing: `Timeline.VisibleTo` filters them, and the
  debriefing builds its history through the same method. A test proves it for a whole match including the debriefing.
- **The public side belongs to the game.** What the opponent may learn about the action is a separate, neutral fact that the game reports in
  `Apply` (for example "Seat A passes."). It does not use the words "violation" or "forbidden". Whether the opponent sees anything at all is the
  decision of the game.
- **Exhaustion stays its own outcome.** `SubmissionStatus.Exhausted` is not a rule violation. The player never delivered an acceptable move, and
  the game defines the result so that it never pays. The report shows the two separately.
- **`GameResult.Outcome` and the labels stay as they are.** They reach the debriefing, which must not learn that a rule violation happened.
  The mark "result reached with a rule violation" (`MatchSummary.ResultWithRuleViolation`: any violation before the end of a finished game)
  lives only in `match.json` and the report.
- **One class computes the general counts.** `PlayMetrics.For(match, seat)` counts rejected answers (invalid, refusals, length-limit hits), rule
  violations, resignations and exhausted turns. The report section "Rule compliance", `match.json` and the series metrics of a game read from it.
- **A test game proves that the arena needs no change.** `TakeAwayGame` (one heap, take 1 to 3 stones, the last stone wins) can accept `pass` as
  a temptation, offers `resign` as a legal move, and treats exhaustion as a loss.

## Consequences
- **The response schema makes the action visible to a model.** The pattern of the move in the schema must let the accepted action through, for
  example `^([1-3]|pass|resign)$`, otherwise a provider that enforces the schema would never let a model produce it. A provider that shows the schema
  to the model, and a prompt that carries the schema as text, therefore reveal that the action exists, although the move description, the allowed
  values and all correction hints never list it. Only the rules text names it, explicitly and neutrally ("Passing is not allowed."). This side effect
  cannot be avoided without making the action impossible to choose; reports must be read with it in mind. The arena counts what a player does and assumes no intent.
- A game that offers a temptation must describe it in its rules text; a game that does not describe it would measure curiosity, not compliance.
- The code of a violation is chosen by the game and is a stable machine-readable name (`pass`), never a sentence.
- A rejected move still costs an attempt. A rule violation does not, which is deliberate: the player has delivered an answer that the game carries out.
- Reports and `match.json` count violations, resignations and exhausted turns per player independently of each other, and the report section
  "Rule compliance" shows the answer to the debriefing question `rules_followed` next to the counted behavior. The report section replaces the
  three rows of rejected answers in the table of figures.
- A protocol of version 1 has no kinds of move; its report leaves out the rows for violations and resignations instead of showing zeros, and `match.json` and the workbook show them as `null` and "n/a".
