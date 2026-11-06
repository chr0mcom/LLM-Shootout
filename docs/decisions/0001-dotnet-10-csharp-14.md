# 0001 - .NET 10 and C# 14

Status: accepted (2026-11-06)

## Context
The project needs a target framework. The rule: newest generally available version, no preview, no release
candidate. On 2026-11-06 the release index lists .NET 11.0 as `go-live` (release candidate 3, SDK
11.0.100-rc.3) and .NET 10.0 as the active LTS (SDK 10.0.403).

## Options
- A: .NET 11 RC with go-live license.
- B: .NET 10 LTS with C# 14.

## Decision
B. The target framework and `LangVersion` live only in `Directory.Build.props`. `global.json` pins the SDK
band (`10.0.100`, `rollForward: latestFeature`).

## Rationale
Readers must be able to run the project with a released SDK. An RC is not generally available.

## Advisor input
Not consulted (rule-based decision).

## Consequences
Before part 0 is delivered (2027-02-11) the project should be updated to .NET 11 if it is GA: change two
properties, bump package versions, rebuild, rerun the tests.
