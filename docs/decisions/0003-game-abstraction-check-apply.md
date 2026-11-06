# 0003 - Game abstraction: Check per seat, Apply per round

Status: accepted (2026-11-06)

## Context
The arena must run six different games (and later a human player and a three-seat game) without being changed.
The games differ in turn order (alternating, simultaneous, phases), hidden information, roles and move shapes.

## Options
- A: `Submit(seat, move)` on the game, one call per seat (draft from the task description).
- B: `Check(turn, move)` without side effects per seat, then one `Apply(all submissions of the round)`.
- C: A state machine inside the arena that games configure.

## Decision
B. `IGame.PendingTurns` lists what is asked now: one entry is sequential, two entries are simultaneous, none
means the game is over. The arena asks every listed seat against the same state, validates each answer with
`Check`, collects, and calls `Apply` once. Simultaneous moves therefore stay hidden by construction.
`TurnRequest` has a `TurnId` and a `Kind` (`talk`, `decide`, `offer`, `respond`, ...). `Check` returns a
normalized move or a stable error code plus a template for the correction hint. After exhausted attempts the arena
reports `SubmissionStatus.Exhausted` and the game decides what follows (default move, loss, skipped turn).
`Apply` returns `GameFact`s with an audience (everyone, one seat, analysis only). Oracle verdicts of Nim are
analysis facts: they reach the protocol but never a prompt.
Game options come as JSON from series files; `IGameDefinition.Create` validates them
(`GameOptionsException`), including role assignment. A rematch with swapped roles is therefore configuration.
Game specific report sections come from an optional `IGameAnalyzer` that reads only protocol events.

## Advisor input
Codex (Sol) and the Opus advisor both confirmed Check-then-Apply, and both asked for `TurnId`/`Kind`, structured
error codes and a normalized move from `Check`. Opus added fact visibility, game options with validation, an
analyzer per game, and role assignment as an option. All adopted.

## Consequences
Games never see raw model answers. `Apply` must tolerate `Exhausted`. Reports for a game never touch the arena.
