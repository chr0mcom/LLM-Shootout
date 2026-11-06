# 0007 - One retry layer, one timeout

Status: accepted (2026-11-06)

## Context
The OpenAI client has its own retry policy and network timeout (100 s by default). The standard resilience
handler of `Microsoft.Extensions.Http.Resilience` brings a 30 s total and a 10 s attempt timeout, and a circuit
breaker. Reasoning models can need minutes. Two stacked retry layers would multiply attempts and cost.

## Decision
- The OpenAI client retries zero times (`ClientRetryPolicy(0)`), and its network timeout is the call timeout plus 30 s.
- A custom resilience pipeline (`ResilienceHandler`) holds exactly two strategies: an outer timeout equal to the
  configured call timeout (default 180 s) and a retry for HTTP 429, 5xx and connection errors (3 retries,
  exponential backoff with jitter, `Retry-After` honored). No circuit breaker: two sequential players do not
  need one.
- A timeout is not retried, because the cancelled request may already be billed. It ends the call as a
  `PlayerFailureException` with `IsTimeout`, uses up one attempt of the turn (ADR 0003 / task 4.4), and costs
  "unknown" in the meter. Content retries (invalid answers) and HTTP retries are separate and both visible:
  the protocol has `Attempt` for the first and `HttpAttempts` for the second.

## Consequences
The call timeout is the one knob (`OpenRouterOptions.CallTimeout`) and it covers HTTP retries. With three
attempts per turn a stuck model can use up to three call timeouts per turn.
