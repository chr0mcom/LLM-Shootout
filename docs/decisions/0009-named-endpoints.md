# 0009 - Named endpoints for OpenAI-compatible APIs

Status: accepted (2026-11-06)

## Context
The first version talked to OpenRouter only. On the development server there is no OpenRouter key, but a gateway
(`cli-proxy`) that exposes the subscriptions of Claude Code and Codex as an OpenAI-compatible API with two base
addresses, one per backend. A duel such as Claude against Codex needs both addresses in one match. Other readers
will have a local server or a company proxy. The key must still never reach a file, a log or a protocol.

## Decision
- A player specification can carry an **endpoint name**: `<endpoint>:<model>`. Names come from the configuration
  section `Endpoints` (file `shootout.settings.json` or `LLMSHOOTOUT_Endpoints__…` variables), plus the built-in
  `openrouter`. Bare slugs stay OpenRouter slugs. A text with a slash before the first colon (`deepseek/deepseek-r1:free`)
  is always a slug.
- An endpoint has a base address, a dialect (`EndpointKind`: `OpenRouter` or `OpenAiCompatible`), the **name of the
  environment variable** that holds the key, and a `FlatRate` flag.
- The dialect decides only what is sent and which model list is expected: OpenRouter keeps `provider.require_parameters`,
  `reasoning.effort` and its headers; other endpoints get `reasoning_effort` and nothing OpenRouter specific. The
  retry layer, the timeout, the raw capture and the protocol are the same for all endpoints (ADR 0007).
- Cost without a reported value: OpenRouter estimates from the model price (ADR 0008). A flat-rate endpoint counts such
  a call as 0 USD. Any other endpoint leaves it unknown, so the spending limit keeps holding back its reserve. The
  safe behavior is the default; "free" must be stated.
- A model list that an endpoint cannot deliver does not stop a match; the first call reports an unknown model.
- Every endpoint caches its model list in its own file.
- The code that talks to a model is not called after OpenRouter any more: the namespace is `LlmShootout.Arena.ModelAccess`, the
  options are `EndpointOptions`, the HTTP builder `ChatClientFactory`, the request handler `EndpointRequestHandler`, the response
  fields `CompletionFields`. The first version of this change kept the old names to avoid touching every test and snippet; the
  article-readiness review pointed out that `OpenRouterOptions` configuring a local gateway reads wrongly in print, and no article
  had appeared yet, so the rename was made. `EndpointKind.OpenRouter`, the configuration section `OpenRouter` and the variable
  `OPENROUTER_API_KEY` keep their names, so no setting of a user changes.

## Consequences
- A match can mix endpoints; the protocol names each player `<endpoint>:<model>`.
- `shootout debrief` and `annotate` need the endpoint configuration that the match used.
- A gateway that ignores settings (the CLI proxy ignores `temperature`) is documented in `docs/endpoints.md`; the
  settings are still recorded as sent.
- Found on the way: `MaxHttpRetries = 0` was allowed by validation but made the resilience pipeline throw. Zero now means "no retry layer".
