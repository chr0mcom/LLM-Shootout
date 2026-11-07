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
environment variables. An example is in [`docs/examples/shootout.settings.cli-proxy.json`](examples/shootout.settings.cli-proxy.json).

```json
{
  "Endpoints": {
    "claude": {
      "BaseAddress": "http://127.0.0.1:8090/v1/claude",
      "ApiKeyVariable": "LLMSHOOTOUT_PROXY_API_KEY",
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
export LLMSHOOTOUT_PROXY_API_KEY=...

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

## The CLI proxy of this server

The gateway of the hosting server offers two backends below one address, so there are two endpoints:

| Endpoint | Base address | Models |
|---|---|---|
| `claude` | `http://127.0.0.1:8090/v1/claude` | `claude-sonnet-latest`, `claude-opus-latest`, `claude-haiku-latest`, … |
| `codex` | `http://127.0.0.1:8090/v1/codex` | `gpt-6.1-sol`, `gpt-6.1-terra`, … |

On the host the proxy listens on the loopback address; inside the Docker network `proxy` it is `http://cli-proxy:8090`.
The proxy requires a bearer key for `chat/completions`. Each consumer has its own key in the allowlist of the
gateway (set up where the gateway is deployed); the example file reads it from `LLMSHOOTOUT_PROXY_API_KEY`.

Things to know about this gateway:

- Calls are covered by the subscriptions of the command line tools: set `FlatRate` to `true`.
- `temperature` and other sampling settings are accepted and **ignored**. Do not draw conclusions from `--temperature`.
- `--reasoning-effort` accepts `low`, `medium` and `high`; `minimal` is rejected with HTTP 400.
- The gateway can switch to the other backend when one is at its limit (failover). The `model` field of the answer
  then still shows the requested model. Check the `x-cli-proxy-failover` header if a result looks odd; for an
  experiment that compares models, prefer a quiet moment or a second look at `/health`.
- Latency is higher than with a plain API (a command line tool is started per call). The default call timeout of
  180 s is usually enough. Measured on 2026-11-07 with Claude Haiku: 12 to 28 seconds per turn, up to 70 seconds when the
  gateway is busy, against 4 to 9 seconds for Codex. `reasoning_effort` low helped only a little. The token numbers are
  large (about 22 000 in, 1 000 to 8 000 out per call) because the command line tool brings its own system prompt and
  does its own thinking; the prompt of the game is about 1 500 tokens. The "thinking" column stays 0 because the
  gateway does not report reasoning tokens separately.
- With a JSON schema, the Claude backend sometimes answers `content: null` and a message "Could not produce valid
  structured output: no JSON object found in the response" (in a test, 4 of 6 long debriefing requests). The arena
  treats that as an invalid answer and asks again (three attempts per turn). The message is written to the protocol as
  `endpoint: …` in the problems of that call. Without a schema the model sometimes declines to answer in JSON, so a
  schema stays on.
- For quick demonstrations, the same model through OpenRouter is much faster and cheap: `anthropic/claude-haiku-5.5` costs
  0.10 USD per million input tokens, a whole match well under one cent.
