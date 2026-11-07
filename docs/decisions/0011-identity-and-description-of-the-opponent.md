# 0011 - Identity and description of the opponent

Status: accepted (2026-11-07)

## Context
The first version could name the opponent model in the prompt (`--reveal-identity`), described the opponent as "another language model"
otherwise, and asked a debriefing question about the opponent's model. The series focuses on game strength and rule compliance. The detail
is not part of the new focus, and a prompt that describes the opponent differently for different kinds of player (language model, random
player, program) makes setups hard to compare.

## Decision
- The system text and the debriefing text describe the opponent as "your opponent" for every kind of player. They do not say whether it is a
  language model, a random player or a program. The templates `opponent.hidden`, `opponent.revealed` and the `{opponent}` placeholder are gone;
  `TurnPromptInput.Opponent`, `TurnExecutor.OpponentModel` and `PromptIdentityFormatter.Format` are gone with them.
- `MatchOptions.RevealIdentity`, `MatchOptions.RedactIdentity`, `--reveal-identity`, `--redact-identity` and the `revealIdentity` field of series
  files are removed. A series file that still has `revealIdentity` fails with the name of the field.
- The debriefing question about the opponent's model and its report section are removed. `QuestionCondition` and `DebriefingQuestion.When` are removed as
  well: `StandardQuestions.For(extra)` has no conditions, and a game decides in `ExtraQuestions(setup)` whether it asks a question.
- `IdentityScanner` and `IdentityGuard` stay as a technical marker. A message that names a model gets `IdentityHits` in the protocol and a mark in
  the history line of the report and in `match.json`. The guard never changes a message, and no report section builds on the marker.
  `PromptIdentityFormatter` becomes `ModelIdentityNames`, which keeps only the slug helpers that the scanner uses.

## Consequences
- All prompt snapshots change once (the system text); the arena template hash in the protocol changes with it.
- Protocols written before this change stay readable: `MatchOptions` in the `match_started` event ignores the removed fields. A test keeps a
  protocol of the earlier format and builds its summary and report.
- The description of the opponent is the same text in every match, so a comparison of two setups never differs in what the player was told about
  its opponent.
