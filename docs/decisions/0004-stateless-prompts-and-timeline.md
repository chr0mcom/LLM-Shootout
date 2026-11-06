# 0004 - Stateless prompts built from one timeline

Status: accepted (2026-11-06)

## Context
Prompts can be built fresh for every turn or grow as a running chat. Message history and move history could
live in the arena and the game separately.

## Options
- A: Running chat per player. Cheap per call, but the prompt depends on provider behavior and cannot be reproduced.
- B: Stateless prompt per turn, rebuilt from the state.

## Decision
B. Every call sends the system prompt (contract, language, rules of the seat) and one user message with the
current view of the game, the timeline from the seat's perspective, the seat's own recent private thoughts, and
the instruction for the turn. A retry appends the seat's invalid answer and the correction hint.
The arena keeps one timeline of public messages and public facts in order, plus the private facts of each seat.
`IGame.ViewFor` only shows the current state (a board, own dice). No game keeps a second history, and prompts,
debriefing and reports all read the same timeline.
Own earlier thoughts are shown (default: the last 10, `ThoughtMemory` option, 0 for none). Reason: the lead
question of the first episode is "does a model keep its word". Without its own earlier notes, a broken promise
could be plain forgetting. Thoughts of the opponent are never shown.

## Advisor input
Both advisors agreed on stateless prompts. Opus pointed out the split-history risk (messages in the arena,
moves in the game) and asked for one timeline; adopted. Codex wanted thoughts off by default to avoid
self-anchoring, Opus wanted the last N. Decision: last 10, configurable, because the memory question above outweighs the anchoring risk.

## Consequences
The prompt grows with the match but is bounded by the match length. Prompt snapshot tests make every change visible.
