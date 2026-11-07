# 0011 - Description of the opponent

Status: accepted (2026-11-07)

## Context
The series focuses on game strength and rule compliance. The detail of who or what the opponent is does not belong to this focus. A prompt
that describes the opponent in one way for a language model and in another way for a random player or a program makes setups hard to
compare, and a prompt that is the same text in every match does not.

## Decision
- The system text and the debriefing text describe the other player as "your opponent" for every kind of player. They do not say whether it
  is a language model, a random player or a program. Templates and options that varied this description are removed, together with the
  placeholder and the helper types that filled it. A series file that still has a removed field fails with the name of the field.
- The debriefing has no question about the opponent's model. `QuestionCondition` and `DebriefingQuestion.When` are removed as well:
  `StandardQuestions.For(extra)` has no conditions, and a game decides in `ExtraQuestions(setup)` whether it asks a question.
- `IdentityScanner` and `IdentityGuard` stay as a technical marker. A message that names a model gets `IdentityHits` in the protocol. The
  guard never changes a message, and neither a report, `match.json` nor a series evaluates the marker.
  `PromptIdentityFormatter` becomes `ModelIdentityNames`, which keeps only the slug helpers that the scanner uses.

## Consequences
- All prompt snapshots change once (the system text); the arena template hash in the protocol changes with it.
- Protocols written before this change stay readable: `MatchOptions` in the `match_started` event ignores fields it does not know. A test
  keeps a synthetic protocol of the earlier format and builds its summary and report.
- The description of the opponent is the same text in every match, so a comparison of two setups never differs in what the player was told
  about its opponent.

Amended 2026-11-07: the marker is not evaluated anywhere. The per-seat count, the line in the report and the flag in the history line are removed; `MessageEvent.IdentityHits` stays in the protocol as a technical field.
