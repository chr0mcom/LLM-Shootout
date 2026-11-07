# 0015 - Every game declares its default for messages between players

Status: accepted (2026-11-07)

## Context
The arena can carry messages between players; whether it does is a setting of a match (`GameSetup.Communication`). The command line option
`--communication` had a fixed default of `on`, a match of a series file had the default `true`, and the web interface started with the box
ticked. That was right while there was one game that is played with messages. A game without messages in its rules would have had to be
started with `--communication off` every time, and a series file would have had to say `"communication": false` in every match.

## Decision
- `IGameDefinition.DefaultCommunication` is the default of a game for this setting. The default implementation is `false`; Split or Steal
  overrides it with `true`, so its behaviour does not change.
- `GameSetup.Communication` stays a `bool`: it is the resolved value that the game and the protocol see.
- The setting is requested, never defaulted, at the three entry points: `--communication` of `play` is nullable, `SeriesMatch.Communication`
  is `bool?` (a missing value in a series file means "the default of the game"), `StartRequest.Communication` was already nullable.
- The resolution is one extension method, `IGameDefinition.ResolveCommunication(bool? requested)`. `play`, `SeriesRunner` and the web
  `RunManager` call it; an explicit value always wins.
- `shootout games` shows the default of each game in a column `Messages`. `/api/config` returns `defaultCommunication` per game, and the
  web form sets its box to that default whenever another game is selected.

## Consequences
- Existing series files and commands behave as before for Split or Steal. A series file for a game with the default `false` needs no
  `communication` entry unless it wants messages.
- The protocol is unchanged: `match_started.communication` records the resolved value.
- A new game decides its default in one line of its definition.
