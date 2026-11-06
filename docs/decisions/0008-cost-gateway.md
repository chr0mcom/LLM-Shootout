# 0008 - Cost metering in one gateway

Status: accepted (2026-11-06)

## Context
The spending limit (default 1 USD per run) must be checked before every model call, and the cost of every call
booked afterwards, for turns, debriefing and judge alike. The cost is known only from the OpenRouter response.

## Decision
`PlayerGateway` is the single door for all player calls. It checks `CostMeter.EnsureWithinLimit()` before the
call, books `ModelCallInfo.CostUsd` after it, and books "unknown" for a failed call to a model (a cancelled request may
still be billed). Unknown costs are counted and shown in the report. `BudgetExceededException` ends the match
cleanly with a valid protocol (`match_aborted`, reason `cost_limit`).

## Alternatives
A `DelegatingChatClient` in the `ChatClientBuilder` pipeline. Rejected: cost is not part of the chat
abstraction (ADR 0006), and a gateway above the player also meters scripted test players and keeps the rule in one
readable class.

## Consequences
The limit is checked between calls, not within one; a single expensive call can overshoot the limit once.
