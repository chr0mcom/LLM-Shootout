# 0014 - The series report summarizes only the metrics a game declares

Status: accepted (2026-11-07)

## Context
The series report listed point totals and averages per seat, invalid answers and refusals per seat, a column for the communication setting of
every match, and, through `IGameAnalyzer.AnalyzeSeries`, whatever sections a game wanted to add. The series focuses on game strength and rule
compliance. Which numbers describe a player over several matches differs from game to game; the arena cannot know it. Sums of points and
generic counters per seat say little when the seats are not equivalent (who starts, who plays first) and mix numbers of different meaning.

## Decision
- `IGameAnalyzer.AnalyzeSeries` is removed. A game declares what a series summarizes: `IReadOnlyList<SeriesMetric> SeriesMetrics`, empty by default.
- `SeriesMetric(Key, Label, Kind, Measure)` is one number. `MetricKind.Sum` adds the contributions of all matches. `MetricKind.Ratio` divides
  the sum of all numerators by the sum of all denominators, so a ratio is weighted by what could have happened, not an average of per-match ratios.
  `Measure(match, seat)` returns a `Fraction(Numerator, Denominator)` or null if the match has nothing to measure for that seat. The label is a
  template of the game and is rendered in the language of the series.
- A ratio whose denominator is 0 is "not applicable": `n/a` in the report and `"value": null` in `series.json`. It is never shown as 0.
- Metrics are grouped by the player name (`match_started.players[seat].name`), so a player's matches on both seats are combined.
- The series report has an overview (game, matches, cost), the list of matches (number, directory, starting seat, outcome, cost) and one table per
  player that contains exactly the declared metrics. A game without declared metrics has no table per player.
- Removed: point totals and averages, the general rows for invalid answers and refusals per seat, `SeriesEntry.Communication`, and every column or
  comparison that depends on the communication setting.
- The directory of a series match is `NN-<seat>-first`, for example `01-A-first`. Directories of older series (`01-talk-A-first`) are still read by
  `shootout report`, which only needs the match directories to contain an `events.jsonl`.

## Consequences
- What a series reports is decided in one place per game, in a few lines that a reader can print.
- `series.json` changes shape: `pointsTotal` is gone, `players` is new, `entries[].communication` became `entries[].firstSeat`.
- The report of a series is computed from the match protocols alone and can be rebuilt with `shootout report`.
