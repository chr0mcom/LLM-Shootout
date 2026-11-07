# CLAUDE.md

LLM Shootout is a .NET console application in which two language models play a game against each other
through OpenRouter or another OpenAI-compatible endpoint. The series studies game strength and rule compliance; the first game is
Nim, a solved game that an oracle rates move by move (`docs/games/nim.md`). The solution contains a reusable arena
(`LlmShootout.Arena`), the games (`LlmShootout.Games`), a command line (`LlmShootout.Cli`), a small web interface
(`LlmShootout.Web`, see `docs/web.md`) and tests (`LlmShootout.Tests`).

## Commands

```bash
dotnet build                                   # warnings are errors
dotnet test                                    # fakes only, no network
dotnet format --verify-no-changes
dotnet run --project src/LlmShootout.Cli -- play nim --a random --b optimal
dotnet run --project src/LlmShootout.Cli -- series shootouts/nim-vs-optimal.json --a random
scripts/smoke.sh                               # every command without an API key
scripts/make-examples.sh                       # regenerates matches/examples/ (Nim, no API key); run it after changes to code or templates
scripts/coverage.sh                            # line coverage of LlmShootout.Arena, threshold 85 %
```

Live tests against OpenRouter run only with `OPENROUTER_API_KEY` and `LLMSHOOTOUT_LIVE_TESTS=1`.

## Conventions

- Target framework and language version are set in one place: `Directory.Build.props`.
- Package versions live in `Directory.Packages.props` (central package management). Only packages with a
  permissive license (MIT, Apache-2.0, BSD).
- Readability beats cleverness: code is printed in a magazine. Short methods, descriptive names.
- Records for events and results, `sealed` by default, primary constructors, collection expressions,
  file-scoped namespaces, nullable everywhere, `CancellationToken` everywhere, `TimeProvider` for time.
- Games are registered explicitly in one place, never discovered by reflection.
- A game that can rate a position implements `IGameOracle`; the arena rates every move with an analysis fact, and the player type `optimal` plays it.
  Facts for analysis never reach a prompt or a debriefing. A figure without a basis is "n/a", never 0.
- Strength is measured by the oracle and by facts in the protocol, not by a judge model.
- Prompts live in versioned template files, not in code.
- The OpenRouter key comes from `OPENROUTER_API_KEY`, the key of another endpoint from the variable named in its
  configuration (`docs/endpoints.md`). Keys never appear in files, logs or protocols.
- Public text (code, comments, docs) is English. Details: `docs/dev/dotnet-baseline.md`.
- Every architecture decision gets an ADR in `docs/decisions/`.
