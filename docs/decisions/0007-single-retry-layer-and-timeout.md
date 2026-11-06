# 0007 - One retry layer, one timeout

Status: accepted (2026-11-06)

## Context
The OpenAI client has its own retry policy and network timeout (100 s by default). The standard resilience
handler of `Microsoft.Extensions.Http.Resilience` brings a 30 s total and a 10 s attempt timeout, and a circuit
breaker. Reasoning models can need minutes. Two stacked retry layers would multiply attempts and cost.

## Decision
- The OpenAI client retries zero times (`ClientRetryPolicy(0)`), and its network timeout is the call timeout plus 30 s.
- A custom resilience pipeline (`ResilienceHandler`) holds exactly two strategies: a retry (outer) for HTTP 429, 5xx
  and failures to establish a connection (3 retries, exponential backoff with jitter, the `Retry-After` header is
  ignored so that a long header cannot stall a match) and, inside it, a timeout of one attempt equal to the configured
  call timeout (default 180 s). No circuit breaker: two sequential players do not need one.
- A timeout is not retried, because the cancelled request may already be billed. A connection that breaks while
  the answer is read is not retried for the same reason. A timeout ends the call as a `PlayerFailureException`
  with `IsTimeout`, uses up one attempt of the turn (ADR 0003 / task 4.4), and counts as "cost unknown" in the meter.
  Exhausted HTTP retries (429, 5xx) abort the match with the HTTP status as reason. Content retries (invalid answers) and HTTP retries are separate and both visible:
  the protocol has `Attempt` for the first and `HttpAttempts` for the second.

## Consequences
The call timeout is the one knob (`OpenRouterOptions.CallTimeout`) and it applies to each HTTP attempt. With three
attempts per turn a stuck model can use up to three call timeouts per turn.
