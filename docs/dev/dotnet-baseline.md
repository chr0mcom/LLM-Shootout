# .NET baseline

Research date: 2026-11-06. All versions were checked on that day.

## Decision

| Item | Choice | Why |
|---|---|---|
| Runtime / SDK | .NET 10 (LTS), SDK 10.0.100 or newer (`global.json` rolls forward to the latest feature band) | .NET 11 is at release candidate 3 (go-live license), not generally available. See [ADR 0001](../decisions/0001-dotnet-10-csharp-14.md). |
| Language | C# 14 | Ships with .NET 10. |
| Where the version lives | `Directory.Build.props` (`TargetFramework`, `LangVersion`) | Updating to .NET 11 is a one-line change. |

Update plan: when .NET 11 reaches GA (expected at .NET Conf, November 2026), set `net11.0` and `LangVersion` 15
in `Directory.Build.props`, bump `global.json`, rebuild. The article part 0 is due 2027-02-11.

## C# 14 features and where they are used

Source: <https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14>

| Feature | What it does | Minimal example | Used here |
|---|---|---|---|
| Extension members | `extension(T source) { ... }` blocks declare extension methods and properties, also static ones. | `extension(IEnumerable<int> s) { public bool IsEmpty => !s.Any(); }` | **yes**: `Seat.A.Opponent` and `Seat.All` in `src/LlmShootout.Arena/Games/Seat.cs` |
| `field` keyword | Compiler-synthesized backing field inside a property accessor. | `public string Name { get; set => field = value.Trim(); }` | **yes**: validated options in `src/LlmShootout.Arena/MatchOptions.cs` (`MaxAttempts`, word limits, `ThoughtMemory`) |
| Null-conditional assignment | `a?.B = c;` assigns only if `a` is not null. | `console?.Verbosity = Verbosity.Quiet;` | not needed: no place where it makes the code clearer |
| `nameof` with unbound generics | `nameof(List<>)` is `"List"`. | `nameof(IChatClient)` style names in log messages. | not needed |
| Implicit span conversions | Arrays convert to spans more naturally. | `ReadOnlySpan<char> s = array;` | not needed |
| Simple lambda parameters with modifiers | `(text, out result) => ...` without types. | see Microsoft docs | not needed |
| Partial constructors and events | Source generators can implement them. | see Microsoft docs | not needed |
| User-defined compound assignment | Operators like `+=` can be overloaded separately. | see Microsoft docs | not needed |

Findings while using extension members: a *static* extension property cannot have an initializer (`{ get; } = ...` gives
CS9282), so `Seat.All` reads a field of the enclosing class; and the analyzer rule CA1708 reports a false positive for two
`extension` blocks in one class (switched off in `.editorconfig`).

Older features used on purpose: collection expressions (`[]`, `[.. items]`) throughout; primary constructors for
services (`TurnExecutor`, `PromptBuilder`, `Debriefer`, `PlayerGateway`) and positional records for events and results;
file-scoped namespaces; list, property and tuple patterns (`SplitOrStealGame.Resolve`, `LiveView.Handle`,
`ChatClientFactory.IsRetryable`); `[GeneratedRegex]` (`MarkdownTemplates`); `[LoggerMessage]` (`MatchLog`);
`TimeProvider` (`MatchRunner`, tests use `FakeTimeProvider`); `AsyncLocal` for the per-call capture (`CallCapture`).

## SDK, runtime and libraries

| Topic | Decision | Source |
|---|---|---|
| Solution format | `.slnx` (XML solution, default in the .NET 10 SDK) | <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-sdk-templates> |
| Package versions | Central Package Management (`Directory.Packages.props`) | <https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management> |
| JSON | `System.Text.Json` with source generation and polymorphic events (`[JsonPolymorphic]`) | <https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism> |
| Hosting | Generic Host for the console application (DI, options, logging) | <https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host> |
| Time | `TimeProvider` everywhere, `FakeTimeProvider` in tests | <https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview> |
| HTTP resilience | `Microsoft.Extensions.Http.Resilience`; standard handler defaults (30 s total, 10 s attempt, 3 retries) do not fit slow reasoning models and are configured explicitly | <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience> |
| Test runner | Microsoft.Testing.Platform, selected in `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`); `dotnet test` works unchanged | <https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-runner-intro> |

## Microsoft.Extensions.AI

Source: <https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai>

- `Microsoft.Extensions.AI.Abstractions` defines `IChatClient`; `Microsoft.Extensions.AI` adds `ChatClientBuilder`
  and `DelegatingChatClient` for middleware.
- `Microsoft.Extensions.AI.OpenAI` adapts the official OpenAI client (`AsIChatClient()`); it works against any
  OpenAI-compatible endpoint, including `https://openrouter.ai/api/v1`.
- Structured output goes through `ChatOptions.ResponseFormat` (`ChatResponseFormat.ForJsonSchema`).
- Usage arrives in `ChatResponse.Usage` (`UsageDetails`). Provider-specific fields (cost, generation id, provider)
  are not part of the abstraction. See [ADR 0006](../decisions/0006-model-access-pipeline.md).

## OpenRouter

Sources: <https://openrouter.ai/docs/use-cases/usage-accounting>, <https://openrouter.ai/docs/features/structured-outputs>,
<https://openrouter.ai/docs/use-cases/reasoning-tokens>, <https://openrouter.ai/api/v1/models>

| Need | Where it is |
|---|---|
| Cost in USD | `usage.cost` in every response, no request option needed (`usage: {include: true}` is deprecated) |
| Reasoning tokens | `usage.completion_tokens_details.reasoning_tokens` |
| Generation id | `id` of the response |
| Actual model and provider | `model` and `provider` in the response body |
| Reasoning text | `choices[].message.reasoning`, structured variant `reasoning_details` |
| Limit or hide reasoning | request field `reasoning: { effort | max_tokens, exclude, enabled }` |
| JSON schema output | `response_format: { type: "json_schema", json_schema: { name, strict, schema } }` |
| Route only to providers that honor the schema | `provider: { require_parameters: true }` |
| Which models support schemas | `supported_parameters` of each model in `/api/v1/models` contains `structured_outputs` (and `response_format`) |
| Prices | `pricing.prompt`, `pricing.completion` in USD per token (string) |

## Packages

Versions and licenses checked on NuGet on 2026-11-06.

| Package | Version | License | Decision |
|---|---|---|---|
| Microsoft.Extensions.AI / .OpenAI | 10.11.0 | MIT | used |
| Microsoft.Extensions.Http.Resilience | 10.11.0 | MIT | used |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | used (CLI) |
| Microsoft.Extensions.Options.DataAnnotations | 10.0.14 | MIT | validated options |
| Microsoft.NET.Sdk.Web (ASP.NET Core shared framework) | 10.0 | MIT | web interface |
| System.CommandLine | 2.0.14 | MIT | used for the command line |
| Spectre.Console | 0.57.2 | MIT | used for console output |
| xunit.v3.mtp-v2 | 4.0.2 | Apache-2.0 | test framework |
| coverlet.MTP | 10.1.0 | MIT | code coverage |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | MIT | `FakeTimeProvider` |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | MIT | starts the web interface in memory for tests |
| Verify | 33.3.4 | OSMF EULA | rejected (not a permissive license), snapshots via a small own helper |
| FluentAssertions 8+ | 8.x | commercial | rejected; xUnit `Assert` is enough |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | Microsoft license text | rejected in favor of coverlet.MTP (MIT) |
| TUnit, MSTest, Shouldly, AwesomeAssertions | - | MIT / BSD / Apache | not needed |

Analyzers: the built-in .NET analyzers at `AnalysisLevel` `latest-recommended` with warnings as errors, plus
`dotnet format` for style. See [ADR 0002](../decisions/0002-packages-and-tooling.md).

## More used features (with location)

| Feature | Where |
|---|---|
| `System.Text.Json` source generation with polymorphic events (`[JsonPolymorphic]`, `[JsonDerivedType]`) | `Protocol/MatchEvents.cs`, `Protocol/ProtocolJson.cs` |
| `System.Text.Json` strict reading (`RespectRequiredConstructorParameters`, `UnmappedMemberHandling.Disallow`) | `Series/SeriesFile.cs` |
| `IChatClient`, `ChatClientBuilder`, structured output (`ChatResponseFormat.ForJsonSchema`) | `ModelAccess/ChatClientFactory.cs`, `OpenRouter/ChatPlayer.cs` |
| `Microsoft.Extensions.Http.Resilience` (`ResilienceHandler`, custom pipeline) | `OpenRouter/ChatClientFactory.cs` |
| Generic Host, options pattern with validation (`ValidateDataAnnotations`), logging to stderr | `src/LlmShootout.Cli/Program.cs` |
| `DelegatingHandler` for request rewriting and raw capture | `OpenRouter/EndpointRequestHandler.cs`, `OpenRouter/RawCaptureHandler.cs` |
| Central Package Management, `.slnx`, Microsoft.Testing.Platform runner | `Directory.Packages.props`, `LlmShootout.slnx`, `global.json` |
| `dotnet test --coverlet` (coverlet.MTP) | `scripts/coverage.sh` |
