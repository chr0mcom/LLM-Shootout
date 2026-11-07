# 0008 - Cost metering in one gateway

Status: accepted (2026-11-06), amended 2026-11-07

## Context
The spending limit (default 1 USD per run) must be checked before every model call, and the cost of every call
booked afterwards, for turns and debriefing alike. The cost is known only from the OpenRouter response.

## Decision
`PlayerGateway` is the single door for all player calls. It checks `CostMeter.EnsureWithinLimit()` before the
call, books `ModelCallInfo.CostUsd` after it, and books "unknown" for a failed call to a model (a cancelled request may
still be billed). Unknown costs are counted and shown in the report. `BudgetExceededException` ends the match
cleanly with a valid protocol (`match_aborted`, reason `cost_limit`).

Cost that OpenRouter did not report is estimated from tokens and the model price; a call with neither is held back with a fixed
reserve (`CostMeter.ReservedUsd`), so the limit still bites if the cost field disappears. A failed call books the cost its error response carried.
If the limit is reached during the debriefing, the match keeps its result and ends with `cost_limit`.

## Alternatives
A `DelegatingChatClient` in the `ChatClientBuilder` pipeline. Rejected: cost is not part of the chat
abstraction (ADR 0006), and a gateway above the player also meters scripted test players and keeps the rule in one
readable class.

## Consequences
The limit is checked between calls, not within one; a single expensive call can overshoot the limit once.

Amended 2026-11-07: the single door serves turns and the debriefing; the judge calls are gone (see [0012](0012-judge-infrastructure-removed.md)).
