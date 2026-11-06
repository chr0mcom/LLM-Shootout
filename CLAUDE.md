# CLAUDE.md

LLM Shootout is a .NET console application in which two language models play a game against each other
through OpenRouter. The solution contains a reusable arena (`LlmShootout.Arena`), the games
(`LlmShootout.Games`), a command line (`LlmShootout.Cli`) and tests (`LlmShootout.Tests`).

## Commands

```bash
dotnet build                                   # warnings are errors
dotnet test                                    # fakes only, no network
dotnet format --verify-no-changes
dotnet run --project src/LlmShootout.Cli -- play split-or-steal --a random --b random
scripts/smoke.sh                               # every command without an API key
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
- Prompts live in versioned template files, not in code.
- The API key comes from `OPENROUTER_API_KEY` and never appears in files, logs or protocols.
- Public text (code, comments, docs) is English. Details: `docs/dev/dotnet-baseline.md`.
- Every architecture decision gets an ADR in `docs/decisions/`.
