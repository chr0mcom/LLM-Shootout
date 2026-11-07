# 0013 - A new standard debriefing, and consistent answers of players without a model

Status: accepted (2026-11-07)

## Context
The series focuses on game strength and rule compliance. The standard debriefing of the first version contained questions that did not concern how a game was played, and a game could add
questions that only fitted that game. Such questions say little about game strength or rule compliance.
Players without a model (`random`, and later players such as `optimal`) answered every question with a random value, which produced
contradictions in the example reports: a player that never broke a rule "admitted" to doing so.

## Decision
- The standard questions are `strategy`, `decisive_move`, `own_mistake`, `opponent_strength` (scale 1 to 5), `rules_followed` (boolean with a
  reason) and `next_time`. They are the same for every game and asked in German or English like all templates.
- A game adds questions of its own through `ExtraQuestions(setup)` and decides there whether to ask them. Game specific questions that did not
  concern how the game was played were removed, and so were the report parts that read them.
- The answers of players without a model come from `DebriefAnswers.Sample`, which is deterministic and takes no random generator: a scale
  gets its middle value, a boolean `false`, a choice its first value, text and reasons the template `debrief.sample.text`. A question can name
  its own answer through `DebriefingQuestion.SampleAnswer`, a JSON literal; `rules_followed` answers `true`.
- A `scripted` player answers as its script says. The author of a script is responsible for answers that fit the moves.

## Consequences
- Every debriefing prompt and every report snapshot changes once; the arena template hash in the protocol changes with it.
- A player without a model never contradicts its own play in a report, so a contradiction between an answer and the recorded facts always
  comes from a model.
- A game that needs another answer for a player without a model sets `SampleAnswer` on its question, for example `false` for a question that
  asks whether a forbidden action was used.
- Protocols of earlier matches keep their answer keys; the report shows whatever keys a protocol contains.
