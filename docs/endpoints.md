# Endpoints: models beyond OpenRouter

By default every model is reached through [OpenRouter](https://openrouter.ai). Any other **OpenAI-compatible API**
(`POST <base>/chat/completions`) can be configured as a named *endpoint*, for example a local server (Ollama, LM Studio, vLLM), a company proxy
or the provider itself. Each player chooses its endpoint, so two players of one match may use different ones.

```bash
shootout play nim --a ollama:llama3.2 --b openai/gpt-4o
```

A player specification is `<endpoint>:<model>`. Without a prefix (or with `openrouter:`) the model is an OpenRouter
slug as before. `random` and `scripted:<file>` stay as they are.

## Configure an endpoint

Create `shootout.settings.json` in the directory you run `shootout` from (the file is ignored by git), or use
environment variables.

```json
{
  "Endpoints": {
    "ollama": {
      "BaseAddress": "http://localhost:11434/v1",
      "FlatRate": true
    },
    "lmstudio": {
      "BaseAddress": "http://localhost:1234/v1",
      "ApiKeyVariable": "LMSTUDIO_API_KEY"
    }
  }
}
```

The same as environment variables (`__` separates levels):

```bash
export LLMSHOOTOUT_Endpoints__ollama__BaseAddress=http://localhost:11434/v1
export LLMSHOOTOUT_Endpoints__ollama__FlatRate=true
```

| Setting | Meaning |
|---|---|
| `BaseAddress` | required. The API root including the version segment; the application appends `chat/completions` and `models` |
| `ApiKeyVariable` | name of the environment variable that holds the key. Default: `LLMSHOOTOUT_<NAME>_API_KEY` (for `ollama`: `LLMSHOOTOUT_OLLAMA_API_KEY`). The key is **never** part of the file. An endpoint without any key is called without credentials |
| `FlatRate` | `true` if calls cost nothing per call (a local model, a flat-rate subscription). A call without a reported cost then counts as 0 USD. Default `false` |
| `Kind` | `OpenAiCompatible` (default) or `OpenRouter` |

The name is case-insensitive. `openrouter`, `random` and `scripted` are reserved and refused, because they already mean something in a player specification.

## Keeping the keys in one place

`scripts/shootout.sh` loads an environment file and then starts the application with all arguments you pass:

```bash
# ~/.config/llm-shootout/env  (chmod 600; or set LLMSHOOTOUT_ENV to another path)
export OPENROUTER_API_KEY=sk-or-...
export LMSTUDIO_API_KEY=...

scripts/shootout.sh play nim --a ollama:llama3.2 --b openai/gpt-4o
```

## What differs from OpenRouter

| | OpenRouter | other endpoint |
|---|---|---|
| Model list | `GET models` with prices and `supported_parameters` | `GET models` with ids only; every model counts as JSON-schema capable, settings are not checked against it |
| Request | `provider.require_parameters`, `reasoning.effort`, `X-Title` and `HTTP-Referer` headers | JSON schema (strict) and `reasoning_effort`; no OpenRouter fields, no extra headers |
| Cost | taken from `usage.cost`, otherwise estimated from the model price | taken from `usage.cost` if present; otherwise 0 USD on a flat-rate endpoint, else unknown (the spending limit holds back a fixed reserve per call) |
| Provider, reasoning tokens | recorded | recorded if the endpoint reports them |
| Without a key | refused | allowed |

Use `shootout models --endpoint ollama` to list the models of an endpoint.

The protocol records the player as `<endpoint>:<model>` and the settings that were sent, not the address or the key.
A repeated debriefing (`shootout debrief`) needs the same endpoint configuration.

## Things to know about OpenAI-compatible servers

- Many servers accept `temperature` and other sampling settings but ignore some of them. The protocol records what was sent,
  not what the server did with it.
- `--reasoning-effort` is sent as `reasoning_effort`. A server that does not know it may answer with HTTP 400.
- A server without structured outputs may answer without JSON or with an empty `content` and an error message. The arena treats
  that as an invalid answer and asks again (three attempts per turn); the message is written to the protocol as `endpoint: …` in
  the problems of that call. A schema stays on, because without one many models decline to answer in JSON.
- A local server is usually slower per call than a hosted API and has no price: set `FlatRate` to `true`, so the spending
  limit does not hold back a reserve per call.
- A name such as `llama3.2` moves with the server's model library. Pin a concrete name for a series that has to stay comparable.
