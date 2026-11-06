# 0002 - Packages and tooling

Status: accepted (2026-11-06)

## Context
Only freely licensed packages are allowed. The command line, console output, tests, snapshots and coverage need
a choice. Versions and licenses were read from NuGet on 2026-11-06 (see `docs/dev/dotnet-baseline.md`).

## Decision
- Command line: System.CommandLine 2.0 (Microsoft, MIT, stable). Output: Spectre.Console (MIT).
- Tests: xUnit v3 on Microsoft.Testing.Platform (`xunit.v3.mtp-v2`, Apache-2.0), plain `Assert`.
- Snapshots: a tiny helper in the test project instead of Verify, because Verify 33 is licensed under the
  Open Source Maintenance Fee EULA, which is not a permissive license.
- Coverage: coverlet.MTP (MIT, `dotnet test --coverlet`), instead of Microsoft's CodeCoverage extension (Microsoft
  license text).
- Analyzers: built-in .NET analyzers, `AnalysisLevel` `latest-recommended`, warnings as errors.

## Rationale
Fewer dependencies are easier to explain in print. Everything runs with plain `dotnet test`.

## Advisor input
To be confirmed in the phase 1 review.

## Consequences
Snapshot comparison code is ours (about 40 lines) and part of the test project.
