# 0006 - Model access: IChatClient on top, HTTP handlers underneath

Status: accepted (2026-11-06)

## Context
The task requires `IChatClient` (Microsoft.Extensions.AI) against OpenRouter, a raw capture of every exchange
without credentials, cost and generation data, OpenRouter specific request fields, and recovery of answers that a
reasoning model put into the reasoning channel. An early prototype with a fake HTTP server (identical JSON shape
to OpenRouter) showed what the abstraction carries in each direction.

## Prototype result
Response side, read from `ChatResponse` after `AsIChatClient()`:

| Field | Available through Microsoft.Extensions.AI? |
|---|---|
| Content text, finish reason | yes |
| Delivered model (`model` of the response) | yes (`ModelId`) |
| Generation id (`id`) | yes (`ResponseId`) |
| Input, output, total tokens | yes (`Usage`) |
| Reasoning tokens | no |
| Cost (`usage.cost`) | no |
| Provider (`provider`) | no |
| Reasoning text (`message.reasoning`) | no |
| Raw object | only the typed OpenAI `ChatCompletion`, unknown fields are dropped |

Request side: `ChatOptions` carries model, temperature, max tokens and the response format. It cannot carry
`provider.require_parameters` or `reasoning`. The adapter writes the JSON schema without `strict: true`.

## Decision
- The player talks to an `IChatClient` built with `ChatClientBuilder` (logging) around the OpenAI chat client.
  The OpenAI client uses an `HttpClient` as its transport, so the HTTP pipeline is ours.
- Handlers, outside in: resilience (ADR 0007), `OpenRouterRequestHandler` (adds `provider.require_parameters`
  and `strict` whenever a JSON schema is sent, `reasoning.effort` and extra headers only when configured),
  `RawCaptureHandler` (stores request and response bodies per call in an `AsyncLocal` `CallCapture`; never
  headers, so the API key cannot leak), network.
- Cost, provider, reasoning text and reasoning tokens are read from the captured response body
  (`OpenRouterFields`), everything else from `ChatResponse`. The raw body wins where both exist.
- Sampling parameters (`temperature`, `reasoning`, max tokens) are sent only when configured; the request body
  in the protocol shows what was sent.
- JSON schema is sent only if the model's `supported_parameters` contains `structured_outputs`. Otherwise the
  format is described in the prompt only. Parsing is always robust (code fences, text around JSON, reasoning
  channel).

## Advisor input
See the phase 3 review in the development log. Both advisors agreed earlier that cross-cutting data should be
captured where it is available.

## Consequences
We depend on the OpenRouter response shape for cost and provider. A change there shows up as unknown cost, which
the budget logic counts and the report lists.
