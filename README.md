# LLM Shootout

**Two language models play a game against each other. They may talk first, and afterwards they are asked what they did and why.**

[![CI](https://github.com/chr0mcom/LLM-Shootout/actions/workflows/ci.yml/badge.svg)](https://github.com/chr0mcom/LLM-Shootout/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10%20LTS-512bd4.svg)](https://dotnet.microsoft.com/download)

Every episode has a lead question, for example *"Does a model keep its word?"* or *"Does it find the winning strategy?"*
The models play through [OpenRouter](https://openrouter.ai) or any other OpenAI-compatible endpoint. Every match leaves a
complete, append-only protocol: the exact prompts, the unchanged answers, the private thoughts, the public messages,
the moves, tokens, cost and latency. A readable report is computed from that protocol.

![The web interface after a finished match: the two players, the result, and the conversation with the private thoughts next to the public messages](docs/images/web-interface.png)

It is a .NET console program with no database, plus a small optional [web interface](#web-interface). The repository
grows by one game per episode of the article series *LLM Shootout* in the magazine [dotnetpro](https://dotnetpro.de)
(author: Stefan Bechtel). You can clone it, set an API key, replay the duels, or let other models compete.

| Game | Lead question | Status |
|---|---|---|
| [Split or Steal](docs/games/split-or-steal.md) | Does a model keep its word? | available |
| Mastermind, Battleship, Nim, Ultimatum game, Liar's dice | | planned, one per episode |

## Why it is built this way

- **Three channels per answer.** A private *thought*, a public *message* and the *move* are kept apart. Only that
  separation makes statements about deception possible, because the thought shows what a model intended when it wrote
  the message.
- **Refusals and illegal moves are data, not errors.** They are counted per player and shown in the report.
- **The protocol is the single source of truth.** Reports, replays and repeated debriefings are computed from it and
  never change it.
- **No secrets, no surprises.** Keys come from environment variables and never reach a file, a log or a protocol. Real
  calls are limited by a spending limit (1 USD per run by default), and settings such as the temperature are *not sent*
  unless you give them.
- **Games are plug-ins.** The arena knows no game, a game knows no model. A new game is a folder with two small classes
  and two template files ([Adding a game](docs/adding-a-game.md)).

## Quick start

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). No API key is needed for the first steps.

```bash
# 1. Get the code
git clone https://github.com/chr0mcom/LLM-Shootout.git && cd LLM-Shootout

# 2. Play a match with two random players (no key, no cost)
dotnet run --project src/LlmShootout.Cli -- play split-or-steal --a random --b random

# 3. Look at the report
less matches/*/report.md

# 4. Use real models: create a key at https://openrouter.ai/keys, find model slugs, play
export OPENROUTER_API_KEY=sk-or-...
dotnet run --project src/LlmShootout.Cli -- models --filter gpt-oss
dotnet run --project src/LlmShootout.Cli -- play split-or-steal --a openai/gpt-oss-20b --b anthropic/claude-haiku-5.5

# 5. Play the standard series of three matches
dotnet run --project src/LlmShootout.Cli -- series shootouts/split-or-steal.json --a openai/gpt-oss-20b --b anthropic/claude-haiku-5.5
```

Real model calls cost money. The default limit is **1 USD per run** (`--max-cost`); the run stops cleanly when it is reached.

No OpenRouter account? Any OpenAI-compatible API works as a named endpoint, and each player can use a different one:
`--a ollama:llama3.2 --b openai/gpt-4o`. `scripts/shootout.sh` loads your keys from one file. See [docs/endpoints.md](docs/endpoints.md).

### Example output

The output of `play split-or-steal --a random --b random --seed 7 --lang en`:

```text
split-or-steal  A: random  vs  B: random  (seed 7, messages on, en)
A: Let us see what happens.
B: Trust me.
A: Let us see what happens.
B: Trust me.
A: Trust me.
B: I will play fair.
A plays steal  $0.0000
B plays steal  $0.0000
  · Decision (Seat A): steal
  · Payout (Seat A): 0 points
  · Decision (Seat B): steal
  · Payout (Seat B): 0 points
Result: both_steal  A: 0 (steal)  B: 0 (steal)  $0.0000
Debriefing A: ok (1 attempt(s))
Debriefing B: ok (1 attempt(s))

Report: matches/2026-11-07_split-or-steal_random_vs_random_01/report.md
```

Finished examples (made with random players) are in [`matches/examples/`](matches/examples/).

## Web interface

Prefer a page to a terminal? `src/LlmShootout.Web` is a small web interface: pick two players, start the match and
watch the messages, private thoughts, moves and the debriefing arrive live (screenshot above). Finished matches can be
opened again and downloaded as an Excel workbook, as a report or as the raw protocol. The page is protected by one
password and has daily limits for matches and money.

```bash
export SHOOTOUT_WEB_PASSWORD='a password with at least 12 characters'
dotnet run --project src/LlmShootout.Web --urls http://127.0.0.1:5088
```

For a deployment with Docker and Traefik (everything specific to your server is a variable in `.env`) see
[docs/web.md](docs/web.md).

## Commands

| Command | What it does |
|---|---|
| `shootout games` | lists the games |
| `shootout models [--filter text] [--schema-only] [--limit n] [--from file] [--endpoint name]` | models of OpenRouter with prices and JSON schema support (needs no key) or of a [configured endpoint](docs/endpoints.md); `--from` reads a saved response, for example `docs/examples/models-sample.json` |
| `shootout play <game> --a <player> --b <player>` | one match |
| `shootout series <file> [--a --b]` | all matches of a series file |
| `shootout replay <match> [--delay ms] [--step] [--show-thoughts]` | plays a stored match back in the console (all at once by default; `--step` waits for a key, only with a terminal) |
| `shootout report <match-or-series>` | rebuilds `match.json` and `report.md` from the protocol |
| `shootout debrief <match>` | asks the players again, into a new file |

A *player* is `random`, `scripted:<file.json>` (fixed answers), `openrouter:<slug>`, just a slug like
`openai/gpt-4o`, or `<endpoint>:<model>` for an endpoint from your [configuration](docs/endpoints.md). Useful options of `play` and `series`:

| Option | Meaning |
|---|---|
| `--communication on\|off` | messages between players (`play`); without it the default of the game applies, `shootout games` shows it |
| `--seed <n>`, `--first-seat A\|B` | reproducibility (`play`); a series takes its seed from the file |
| `--lang de\|en` | prompt language (default `de`) |
| `--game-option key=value` | game specific option, repeatable; the value is JSON if it reads as JSON, otherwise text (`pot=1000`, `misere=true`, `heaps=[1,4,5]`, `temptation=pass`, `x="1"` for the text 1; `play` only, a series takes them from the file) |
| `--thought-memory <n>` | how many of its own earlier thoughts a player sees again (default 10, 0 for none) |
| `--max-cost <usd>` | spending limit for the run (default 1) |
| `--no-debrief` | skip the questions after the game |
| `--show-thoughts` | also print the private thoughts of the players while playing |
| `--quiet` | print only the resulting directory (for scripts) |
| `--out <dir>` | where to write the match directories (default `matches`) |
| `--temperature`, `--reasoning-effort`, `--max-output-tokens`, `--timeout` | model settings; **not sent unless given** |

Exit codes: `0` success, `2` wrong usage, or a problem with the setup or the network (the message says what to do), `3` the run was aborted or ended early (cost
limit, timeout, failure), `130` cancelled with Ctrl+C, `1` unexpected error. Every command also runs without a terminal, with redirected input and output.

## What you get per match

```text
matches/<date>_<game>_<a>_vs_<b>_01/
├─ events.jsonl   complete protocol: exact prompts, unchanged answers, reasoning, tokens, cost, latency, generation ids
├─ match.json     summary and key figures
└─ report.md      readable account: history, private thoughts kept apart, figures, debriefing side by side
```

The three channels of an answer are kept apart: a private **thought** (only the player and the protocol see it), a
public **message** (the opponent sees it) and the **move** (checked by the rules). This separation is what makes
statements about deception possible. Refusals and illegal moves are data, not errors: they are counted per player.
Details: [protocol format](docs/protocol-format.md), [prompt contract](docs/prompt-contract.md).

## How it is built

An **arena** runs any game and knows nothing about particular games; a **game** knows its rules and nothing about
models, prompts or files. A new game is a folder with two small classes and two template files:
[Adding a game](docs/adding-a-game.md). The architecture, with diagrams and a table showing how the planned
games fit, is in [docs/architecture.md](docs/architecture.md); the decisions behind it are in [docs/decisions/](docs/decisions/).

Highlights: `IChatClient` (Microsoft.Extensions.AI) over an HTTP pipeline with exactly one retry layer, a raw capture
of every exchange without credentials, cost accounting from OpenRouter usage data, a spending limit, seeds for
everything random, versioned prompt files, typed debriefing questions, and tests that need no network
(`dotnet test`; coverage of the arena is above 85 %).

## Development

```bash
dotnet build                        # warnings are errors
dotnet test                         # fakes only; live tests need OPENROUTER_API_KEY and LLMSHOOTOUT_LIVE_TESTS=1
dotnet format --verify-no-changes
scripts/smoke.sh                    # every command without an API key
scripts/coverage.sh                 # line coverage of LlmShootout.Arena, fails below 85 %
```

See [docs/dev/dotnet-baseline.md](docs/dev/dotnet-baseline.md) for the .NET and C# versions, the packages, and where the new language features are used.

## Documentation

| Topic | Where |
|---|---|
| How the arena, the games and the model access fit together | [docs/architecture.md](docs/architecture.md) |
| Why it was decided that way (ten decision records) | [docs/decisions/](docs/decisions/) |
| The rules, prompts and analysis of Split or Steal | [docs/games/split-or-steal.md](docs/games/split-or-steal.md) |
| What a model is asked, and how its answer is read | [docs/prompt-contract.md](docs/prompt-contract.md) |
| The files of a match and the event format | [docs/protocol-format.md](docs/protocol-format.md) |
| OpenRouter and other OpenAI-compatible servers as named endpoints | [docs/endpoints.md](docs/endpoints.md) |
| The web interface and its deployment | [docs/web.md](docs/web.md) |
| Writing a new game | [docs/adding-a-game.md](docs/adding-a-game.md) |
| .NET and C# versions, packages, language features | [docs/dev/dotnet-baseline.md](docs/dev/dotnet-baseline.md) |

## Origin

LLM Shootout is a new, much lighter build on ideas from *AI Behavior*, a research platform that Stefan Bechtel built for a
research group at the University of Cologne to let language models play a public goods game after Fehr and Gächter
(AiBehavior.PublicGoods, MIT). Taken over are the concepts (three separate channels per
answer, refusal as data, complete raw protocol, debriefing from the player's own point of view) and a few small files,
marked as such in their headers.

## License

MIT, Copyright (c) 2026 Stefan S. Bechtel. See [LICENSE](LICENSE).
