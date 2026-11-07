# 0017 - A self-assessment per turn, and protocol version 2

Status: accepted (2026-11-07)

## Context
The series focuses on game strength and rule compliance. Strength includes the question whether a player judges its own position correctly.
A game with an oracle can say for every position whether the player to move is winning or losing; if the player states how it rates the
position, the two numbers can be compared. The answer contract had three fields (`thought`, `message`, `move`), and the thought is free text
that cannot be compared with a number without interpretation. A fourth field gives a number that needs none.

## Decision
- The answer gets a fourth, optional field `estimate`: a whole number from 0 to 100. Like the other fields it is governed by a rule per turn
  (`TurnRequest.Estimate`: `Forbidden`, `Optional` or `Required`), and it is `Forbidden` by default, so a turn that does not ask for it
  is exactly what it was.
- A turn that asks for an estimate names what the number stands for (`TurnRequest.EstimateMeaning`, a template of the game, for example "your
  chance of winning in percent"). The arena words only the scale. `TurnExecutor` refuses a turn with an estimate and no meaning.
- `AnswerEvaluator` reads a JSON number or number text and accepts whole numbers from 0 to 100 (`55.0` is 55). Anything else, and a missing value
  in a required field, is a violation with the keys `violation.estimate` and `allowed.estimate`, and gets a correction hint in the earlier
  attempts. A value in a forbidden field is rejected like in every other field.
- **In the last attempt a missing or unusable estimate is discarded, not rejected.** The move is played and the turn has no estimate. Rejecting
  the answer would exhaust the attempts over a side field and let a formal slip decide the game, the same reason for which a long message is
  shortened in the last attempt. The discard is recorded as the problem `estimate [discarded]: ...` of the call, so it stays visible in the
  protocol and can be counted.
- `ResponseSchema.ForTurn` adds `estimate` (`{"type": ["integer", "null"], "minimum": 0, "maximum": 100}`, without `null` if required) to
  `properties` and `required` only if the turn is not forbidden. All other schemas, and with them all prompt snapshots, stay as they were.
- The line that shows the shape of the answer is put together from the keys of the turn (`thought`, `message` if there are messages between
  players, `move`, `estimate` if asked) instead of two fixed templates, because the number of combinations would otherwise double with every field.
- The value is recorded in `TurnEvent.Estimate` (and the text as read in `ParsedFields.Estimate`). `TurnSubmission` does not carry it: the rules of
  a game never depend on it. Players without a model answer with `rng.NextInt(101)` when the turn asks for an estimate.
- **Protocol version 2.** `ProtocolVersion.Current` is 2. The version also rises for a new optional field when it extends what a protocol can
  answer, not only for changes that break readers (the rule in `docs/protocol-format.md` said the opposite). The reader accepts versions 1 and 2:
  version 1 events have no estimate and read as "not recorded". A protocol of a newer version is refused with a message that names its version
  and the highest one this program reads. A version 1 protocol is kept in the tests (`Protocol/Legacy/events-v1.jsonl`).
  A line without `schemaVersion` counts as version 1 and is read as such; a version below 1 is refused.

## Consequences
- Prompts, schemas and protocols of games that do not ask for an estimate are unchanged, apart from the version number in the protocol.
- A report can compare the estimate with the oracle value of the position (calibration) for every game that has both.
- The schema states the range with `minimum` and `maximum` on an integer. A provider that does not accept these keywords for integers would
  reject the request with an HTTP error. The arena checks the range itself, so the keywords are only a hint to the model; if a provider
  rejects them in practice, `ResponseSchema.EstimateSchema` is the one place to change.
- Readers of older versions of the program cannot open a version 2 protocol. The version number says so instead of failing in the middle of a file.
