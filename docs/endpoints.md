# Endpoints: models beyond OpenRouter

By default every model is reached through [OpenRouter](https://openrouter.ai). Any other **OpenAI-compatible API**
(`POST <base>/chat/completions`) can be configured as a named *endpoint*, for example a local gateway, a company proxy
or the provider itself. Each player chooses its endpoint, so two players of one match may use different ones.

```bash
shootout play split-or-steal --a claude:claude-sonnet-latest --b codex:gpt-6.1-sol
```

A player specification is `<endpoint>:<model>`. Without a prefix (or with `openrouter:`) the model is an OpenRouter
slug as before. `random` and `scripted:<file>` stay as they are.

## Configure an endpoint

Create `shootout.settings.json` in the directory you run `shootout` from (the file is ignored by git), or use
environment variables. An example is in [`docs/examples/shootout.settings.gateway.json`](examples/shootout.settings.gateway.json).

```json
{
  "Endpoints": {
    "claude": {
      "BaseAddress": "http://127.0.0.1:8090/v1/claude",
      "ApiKeyVariable": "LLMSHOOTOUT_GATEWAY_API_KEY",
      "FlatRate": true
    }
  }
}
```

The same as environment variables (`__` separates levels):

```bash
export LLMSHOOTOUT_Endpoints__claude__BaseAddress=http://127.0.0.1:8090/v1/claude
export LLMSHOOTOUT_Endpoints__claude__FlatRate=true
```

| Setting | Meaning |
|---|---|
| `BaseAddress` | required. The API root including the version segment; the application appends `chat/completions` and `models` |
| `ApiKeyVariable` | name of the environment variable that holds the key. Default: `LLMSHOOTOUT_<NAME>_API_KEY` (for `claude`: `LLMSHOOTOUT_CLAUDE_API_KEY`). The key is **never** part of the file. An endpoint without any key is called without credentials |
| `FlatRate` | `true` if calls are covered by a subscription. A call without a reported cost then counts as 0 USD. Default `false` |
| `Kind` | `OpenAiCompatible` (default) or `OpenRouter` |

The name is case-insensitive. `openrouter`, `random` and `scripted` are reserved and refused, because they already mean something in a player specification.

## Keeping the keys in one place

`scripts/shootout.sh` loads an environment file and then starts the application with all arguments you pass:

```bash
# ~/.config/llm-shootout/env  (chmod 600; or set LLMSHOOTOUT_ENV to another path)
export OPENROUTER_API_KEY=sk-or-...
export LLMSHOOTOUT_GATEWAY_API_KEY=...

scripts/shootout.sh play split-or-steal --a claude:claude-haiku-latest --b openai/gpt-4o
```

## What differs from OpenRouter

| | OpenRouter | other endpoint |
|---|---|---|
| Model list | `GET models` with prices and `supported_parameters` | `GET models` with ids only; every model counts as JSON-schema capable, settings are not checked against it |
| Request | `provider.require_parameters`, `reasoning.effort`, `X-Title` and `HTTP-Referer` headers | JSON schema (strict) and `reasoning_effort`; no OpenRouter fields, no extra headers |
| Cost | taken from `usage.cost`, otherwise estimated from the model price | taken from `usage.cost` if present; otherwise 0 USD on a flat-rate endpoint, else unknown (the spending limit holds back a fixed reserve per call) |
| Provider, reasoning tokens | recorded | recorded if the endpoint reports them |
| Without a key | refused | allowed |

Use `shootout models --endpoint claude` to list the models of an endpoint.

The protocol records the player as `<endpoint>:<model>` and the settings that were sent, not the address or the key.
A repeated debriefing (`shootout debrief`) needs the same endpoint configuration.

## A subscription gateway (the author's setup)

The author runs a small gateway that exposes a Claude Code and a Codex subscription as OpenAI-compatible chat
completion APIs. It is not part of this repository; it only serves as an example of an endpoint that is neither
OpenRouter nor a local model. It offers two backends below one address, so there are two endpoints:

| Endpoint | Base address | Models |
|---|---|---|
| `claude` | `http://gateway:8090/v1/claude` | `claude-sonnet-latest`, `claude-opus-latest`, `claude-haiku-latest`, … |
| `codex` | `http://gateway:8090/v1/codex` | `gpt-6.1-sol`, `gpt-6.1-terra`, … |

The gateway requires a bearer key for `chat/completions`; the example file reads it from
`LLMSHOOTOUT_GATEWAY_API_KEY`. A ready-made settings file is
[`docs/examples/shootout.settings.gateway.json`](examples/shootout.settings.gateway.json), and
[`docker-compose.gateway.yml`](../docker-compose.gateway.yml) wires the same two endpoints into the web container.

Things to know about a gateway of this kind:

- Calls are covered by the subscriptions of the command line tools: set `FlatRate` to `true`.
- `temperature` and other sampling settings are accepted and **ignored**. Do not draw conclusions from `--temperature`.
- `--reasoning-effort` accepts `low`, `medium` and `high`; `minimal` is rejected with HTTP 400.
- The gateway can switch to the other backend when one is at its limit (failover). The `model` field of the answer
  then still shows the requested model. Check the `x-cli-proxy-failover` header if a result looks odd; for an
  experiment that compares models, prefer a quiet moment or a second look at `/health`.
- A command line tool is started per call, so latency is higher than with a plain API, and a `*-latest` name moves
  with the tool: on 2026-11-07 `claude-haiku-latest` changed from Haiku 4.5 to Haiku 5.5. Pin a concrete model name
  for a series that has to stay comparable.
- The first measurements (2026-11-07, Claude Haiku) were poor: 12 to 28 seconds per turn, up to 70 seconds when the
  gateway was busy, about 22 000 prompt tokens per call, and debriefings that failed because the model answered the
  role-play prompt in prose. The cause was that every call ran as a full coding-agent session, with the agent's own
  system prompt, tools and memory. The gateway now runs plain chat calls with the caller's system prompt and nothing
  else (no tools, no web search unless the request asks for it, no memory, no session files): a turn takes about
  5 seconds, a debriefing about 11 seconds, and the prompt is about 1 500 tokens, the size of the game prompt. The
  "thinking" column stays 0 because the gateway does not report reasoning tokens separately.
- With a JSON schema, a gateway may answer `content: null` and a message such as "Could not produce valid
  structured output: no JSON object found in the response" (before the fix described above, 4 of 6 long debriefing
  requests). The arena treats that as an invalid answer and asks again (three attempts per turn). The message is
  written to the protocol as `endpoint: …` in the problems of that call. Without a schema a model sometimes declines
  to answer in JSON, so a schema stays on.
- For quick demonstrations, the same model through OpenRouter is much faster and cheap: `anthropic/claude-haiku-5.5` costs
  0.10 USD per million input tokens, a whole match well under one cent.
