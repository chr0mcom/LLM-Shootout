# LLM Shootout

Two language models play a game against each other through [OpenRouter](https://openrouter.ai). During the game they may
send each other short messages, and afterwards they are asked about their behavior. Every episode has a lead question,
for example *"Does a model keep its word?"* or *"Does it find the winning strategy?"*

The application is a .NET console program with no database and no web UI. The repository grows by one game per
episode of the article series *LLM Shootout* in the magazine [dotnetpro](https://dotnetpro.de) (author: Stefan Bechtel).
You can clone it, set an API key, replay the duels, or let other models compete.

| Game | Lead question | Status |
|---|---|---|
| [Split or Steal](docs/games/split-or-steal.md) | Does a model keep its word? | available |
| Mastermind, Battleship, Nim, Ultimatum game, Liar's dice | | planned, one per episode |

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

### Example output

```text
split-or-steal  A: random  vs  B: random  (seed 7, communication on, en)
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

Report: matches/2026-11-06_split-or-steal_random_vs_random_01/report.md
```

Finished examples (made with random players) are in [`matches/examples/`](matches/examples/).

## Commands

| Command | What it does |
|---|---|
| `shootout games` | lists the games |
| `shootout models [--filter text] [--schema-only] [--limit n] [--from file]` | OpenRouter models with prices and JSON schema support (needs no key; `--from` reads a saved response, for example `docs/examples/models-sample.json`) |
| `shootout play <game> --a <player> --b <player>` | one match |
| `shootout series <file> [--a --b]` | all matches of a series file |
| `shootout replay <match> [--delay ms] [--step] [--show-thoughts]` | plays a stored match back in the console (all at once by default; `--step` waits for a key, only with a terminal) |
| `shootout report <match-or-series>` | rebuilds `match.json` and `report.md` from the protocol |
| `shootout debrief <match>` | asks the players again, into a new file |
| `shootout annotate <match-or-series> --judge <model>` | lets a judge model rate the matches (derived file) |

A *player* is `random`, `scripted:<file.json>` (fixed answers), `openrouter:<slug>`, or just a slug like
`openai/gpt-4o`. Useful options of `play` and `series`:

| Option | Meaning |
|---|---|
| `--communication on\|off` | may the players talk (`play`, default on) |
| `--reveal-identity` | name the opponent model in the prompt (default: hidden) |
| `--seed <n>`, `--first-seat A\|B` | reproducibility (`play`); a series takes its seed from the file |
| `--lang de\|en` | prompt language (default `de`) |
| `--game-option key=value` | game specific option, repeatable (for example `pot=1000`; `play` only, a series takes them from the file) |
| `--redact-identity` | replace model names in public messages by a placeholder (default: only mark them) |
| `--thought-memory <n>` | how many of its own earlier thoughts a player sees again (default 10, 0 for none) |
| `--max-cost <usd>` | spending limit for the run (default 1) |
| `--no-debrief` | skip the questions after the game |
| `--show-thoughts` | also print the private thoughts of the players while playing |
| `--quiet` | print only the resulting directory (for scripts) |
| `--out <dir>` | where to write the match directories (default `matches`) |
| `--temperature`, `--reasoning-effort`, `--max-output-tokens`, `--timeout` | model settings; **not sent unless given** |

Exit codes: `0` success, `2` wrong usage (the message says what to do), `3` the run was aborted or ended early (cost
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

## Origin

LLM Shootout is a new, much lighter build on ideas from *AI Behavior*, a research platform that Stefan Bechtel built for a
research group at the University of Cologne to let language models play a public goods game after Fehr and Gächter
(AiBehavior.PublicGoods, MIT). Taken over are the concepts (three separate channels per
answer, refusal as data, complete raw protocol, debriefing from the player's own point of view) and a few small files,
marked as such in their headers.

## License

MIT, Copyright (c) 2026 Stefan S. Bechtel. See [LICENSE](LICENSE).
