# 0012 - The judge infrastructure is removed

Status: accepted (2026-11-07)

## Context
The first version could let a judge model rate a finished match: the command `shootout annotate`, the `Annotator` with `IAnnotationTask`
and `annotation.<task>.<judge>.json` files next to the protocol, `CallPurpose.Judge`, and one task for Split or Steal. The series focuses on
game strength and rule compliance. Both can be derived from facts that the arena records: a game with an oracle rates every move
exactly, the arena records rule violations and exhausted attempts as facts, and the debriefing answers sit in the protocol next to them.

## Decision
- The command `annotate`, the folder `Arena/Annotation/`, `IGameDefinition.AnnotationTask`, `MatchRecord.Annotations`,
  `CallPurpose.Judge`, `DerivedCommands.BuildAnnotate`, the judge task and templates of Split or Steal and the tests that covered them are removed.
- Game strength and rule compliance are derived deterministically from recorded facts. A judge model would state the same things less
  reproducibly, and every repetition would cost money.
- The report sections of Split or Steal that were built on judge assessments are removed with the task. The rest of the Split or Steal
  analysis is reduced in later steps.
- Old annotation files next to a protocol are ignored; the protocol itself is unchanged and stays readable.

## Consequences
- The arena has one kind of model call less. `PlayerGateway` serves turns and the debriefing.
- A match report is computed from `events.jsonl` alone, with no optional files that change its content.
- A user who wants a model-based rating can still read `events.jsonl` and send what they need to a model of their own; the arena does not do it.
