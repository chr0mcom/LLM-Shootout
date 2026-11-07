# Protocol format

Every match writes a directory. The protocol is the single source of truth: reports, replays and repeated
debriefings are computed from it, and nothing computed later ever changes it.

```text
matches/2026-11-20_nim_gpt-oss-20b_vs_optimal_01/
├─ events.jsonl                  the protocol, one JSON event per line, append-only
├─ match.json                    summary: result, key figures, cost, duration
├─ report.md                     readable report
└─ debrief.<timestamp>.jsonl     answers of a repeated debriefing (optional, `shootout debrief`)
```

A series is a directory `<date>_<series>_<a>_vs_<b>_series/` with one match directory per entry of the series file (`01-A-first`,
`02-B-first`: the number and the seat that starts), plus `series.json` and `series-report.md`. `<series>` is the name of the series file
without `.json` (`nim-misere` for `shootouts/nim-misere.json`), so two series of the same game with the same players get different
directories; a series that was not read from a file uses the id of the game. A second run on the same day gets the ending `-2`.

## Guarantees

- **Append-only and flushed after every event.** A match that is aborted (cost limit, timeout, failure, Ctrl+C)
  leaves a valid file that ends with a `match_aborted` event.
- **Raw data is kept.** The exact prompt, the unchanged answer, the reasoning text if the provider delivered one, the
  requested and delivered model, the OpenRouter generation id, tokens, cost, latency and the raw HTTP exchange (without headers, so without the API key).
- **Derived data lives elsewhere.** `report.md`, `match.json` and repeated debriefings are separate
  files. A test checks that `events.jsonl` stays byte-identical.
- **Readable.** Umlauts are written as they are (not as `ä`), so lines can be quoted.

## Versioning

Every line carries `"schemaVersion"`. The current version is **2**. The version is raised whenever the shape of an
event changes in a way that breaks readers (a field is renamed, removed, or changes its meaning), and also when a new
optional field extends what the protocol means, so that a reader can tell from the version which questions a protocol can
answer. Older versions stay readable: the reader accepts every version from `ProtocolVersion.Oldest` (1) up to
`ProtocolVersion.Current` (2), and a missing field reads as "not recorded". A protocol of a newer version is refused with a
message that names both versions. Readers must ignore unknown fields. `ProtocolVersion.Current` is the constant in code.

| Version | Change |
|---|---|
| 1 | First format. |
| 2 | The self-assessment of a turn: `estimate` in `turn` events (and in `model_call.parsed`), and the kind of an accepted move: `moveKind` in `turn` events, plus the analysis fact `rule_violation`. Version 1 events have none; a missing `moveKind` reads as a regular move. |

A line without `schemaVersion` counts as version 1. A version below 1 is refused.

Naming: property names are camelCase; enum values are written by name (`Turn`, `Everyone`, `A`); keys of
dictionaries are written unchanged (`A`, `B`, `rules_followed`, `nim`).

## Events

All events have `type`, `at` (UTC timestamp) and `schemaVersion`.

### `match_started`

First line. Everything needed to understand and repeat the match.

| Field | Meaning |
|---|---|
| `gameId`, `seed`, `language`, `communication`, `firstSeat` | the game and its setup |
| `gameOptions` | game specific options from the series file, or absent |
| `players` | per seat: `name` (for example `openrouter:openai/gpt-oss-20b`, `ollama:llama3.2`, `random` or `optimal`; the prefix is the [endpoint](endpoints.md)), `requestedModel`, and `settings` (temperature, reasoning effort, output limit, call timeout, only those that are sent; a repeated debriefing uses them again) |
| `templateHashes` | hash of the template file of the arena and of the game, for the language used |
| `options` | the `MatchOptions`: attempts, word limits, thought memory, scanner settings, cost limit, refusal keywords |
| `appVersion`, `gitCommit` | version and short commit of the application |

### `model_call`

One call to a player: a turn or a debriefing.

| Field | Meaning |
|---|---|
| `turnId`, `purpose` (`Turn`, `Debrief`), `seat`, `attempt` | which call |
| `messages` | the complete prompt as sent: `role` and `text` of every message |
| `rawContent`, `rawReasoning` | the unchanged answer and the reasoning channel |
| `parsed` | `thought`, `message`, `move`, `estimate` (as text, version 2), `fromReasoning`, `extraKeys` as read by the arena (turns only) |
| `class` | `valid`, `invalid`, `refusal`, `token_limit`, `technical_error` |
| `violations` | what was wrong, one line per field; an estimate that was dropped in the last attempt appears as `estimate [discarded]: ...` although the answer counts as valid |
| `call` | `requestedModel`, `deliveredModel`, `provider`, `generationId`, `promptTokens`, `completionTokens`, `reasoningTokens`, `costUsd`, `estimatedCostUsd`, `latency`, `finishReason`, `httpAttempts` |
| `costUnknown` | true if the endpoint reported no cost and none could be estimated (the call is held back with a reserve) |
| `error` | text of a technical error |
| `rawExchange` | request and response body of the last HTTP attempt, without headers |

```json
{"type":"model_call","turnId":"move-1","purpose":"Turn","seat":"A","attempt":1,"messages":[{"role":"System","text":"..."},{"role":"User","text":"..."}],"rawContent":"{\"thought\":\"...\",\"move\":\"3:1\",\"estimate\":75}","parsed":{"thought":"...","move":"3:1","estimate":"75","fromReasoning":false,"extraKeys":[]},"class":"valid","violations":[],"costUnknown":false,"at":"2026-11-06T10:14:10.3674841+00:00","schemaVersion":2}
```

### `message`

A public message became visible. `text` is absent for silence.

```json
{"type":"message","turnId":"move-1","seat":"A","text":"Your move.","silent":false,"truncated":false,"identityHits":[],"at":"...","schemaVersion":2}
```

`identityHits` entries look like `opponent:Provider:ChatGPT` (`self`, `opponent` or `ambiguous`; the marker type comes from the scanner). The field is a technical marker; no report, `match.json` or series evaluates it.

### `turn`

The final outcome of a turn of a seat: the move, the status (`played` or `exhausted`), the private `thought`, the
self-assessment `estimate` (version 2: a whole number from 0 to 100, absent if the turn did not ask for one or none was usable), the
kind of the move `moveKind` (version 2: `Resignation` or `RuleViolation`, absent for a regular move and for a turn without a move), and
counters: `attempts`, `invalidAnswers`, `refusals`, `tokenLimitHits`.

A `RuleViolation` is a move that the game carries out although its rules forbid it (see [ADR 0018](decisions/0018-rule-violation-an-accepted-but-forbidden-action.md)).
The arena accepts it at once, without a correction hint and without using up another attempt, and writes the fact `rule_violation`
(below). A `Resignation` is a legal move with which a player gives the game up; the game decides what follows. `status: exhausted`
is neither: the player never delivered an acceptable move, and the turn has no `moveKind`.

```json
{"type":"turn","turnId":"move-1","kind":"take","seat":"A","move":"3:1","status":"played","thought":"Random move.","thoughtTruncated":false,"attempts":1,"invalidAnswers":0,"refusals":0,"tokenLimitHits":0,"estimate":75,"at":"...","schemaVersion":2}
```

### `fact`

Something the game reports. `audience` is `Everyone`, `Seat` (only the named seat sees it in a prompt) or
`Analysis` (never in a prompt, for example the rating of a move by an oracle). `text` is the neutral wording ("Seat A"),
`template` the key and values so that it can be reworded for a viewer, `data` machine readable details. Facts of a round are filed under the `turnId` of the turn of their seat; hidden starting state (dice, a drawn code) comes from `IGame.Start()` and has `turnId` `start`.

The arena itself writes facts: first `rule_violation`, with the audience `Analysis`, for every turn whose `moveKind` is `RuleViolation`.
It sits right after the `turn` event (and its `message` event) and before the facts of the game for that round, has the acting seat
as `seat` and the `turnId` of the turn, and carries the `data` keys `code` (the name of the broken rule, chosen by the game), `move`
and `turn`. It is never rendered into a prompt or into a debriefing. What the opponent may learn about the move is a separate, neutral
fact that the game itself reports (for example "Seat A passes.", with the audience `Everyone`).

If the game has an oracle (`IGameOracle`), the `data` of a rule violation also has the key `value`: the position before the move
(`won`, `lost`, `drawn` or `unknown`).

A game with an oracle gets a second arena fact: `oracle`, with the audience `Analysis`, for every turn with the status `played` and a move.
It follows the `turn` event (and a `rule_violation` fact of the same turn) and precedes the facts of the game for that round. Its `data`:

| Key | Meaning |
|---|---|
| `value` | the position before the move for the seat that moved: `won`, `lost`, `drawn` or `unknown` |
| `move` | the move as the game normalized it |
| `best` | all best moves separated by commas, at most 20; empty in a lost or unknown position |
| `optimal` | `true` or `false` for a regular move in a won or drawn position, otherwise `n/a` (lost or unknown position, resignation, rule violation) |
| `rated` | `false` for a resignation and a rule violation, otherwise `true` |
| `turn` | the turn id |

The text is worded in the language of the match (German: "Lage gewonnen", "optimal: ja"); `values` and `data` keep the machine-readable
values `won`, `lost`, `drawn`, `unknown` and `true`, `false`, `n/a`.
A turn that ended `exhausted` has no `oracle` fact, and neither has a protocol of a game without an oracle or one written before the
oracle existed; every figure that depends on it is "not applicable" there. The fact is never rendered into a prompt or a debriefing.

```json
{"type":"fact","turnId":"move-1","kind":"oracle","audience":"Analysis","seat":"A","text":"Oracle, seat A, move-1: position won, move `1`, optimal: yes. Best moves: 1.","template":{"key":"fact.oracle","values":{"seat":"A","turn":"move-1","value":"won","move":"1","optimal":"true","best":"1"}},"data":{"value":"won","move":"1","best":"1","optimal":"true","rated":"true","turn":"move-1"},"at":"...","schemaVersion":2}
```

```json
{"type":"fact","turnId":"move-1","kind":"rule_violation","audience":"Analysis","seat":"A","text":"Seat A played `pass` in move-1. The rules forbid it (code: pass).","template":{"key":"fact.rule_violation","values":{"seat":"A","turn":"move-1","move":"pass","code":"pass"}},"data":{"code":"pass","move":"pass","turn":"move-1"},"at":"...","schemaVersion":2}
```

```json
{"type":"fact","turnId":"move-1","kind":"take","audience":"Everyone","seat":"A","text":"Move (Seat A): 1 from heap 3. Heaps now: 3, 4, 4","template":{"key":"fact.take","values":{"heap":"3","count":"1","heaps":"3, 4, 4"}},"data":{"heap":"3","count":"1","heaps_after":"3,4,4"},"at":"...","schemaVersion":2}
```

### `match_finished`

The game ended: `result` with `outcome`, per-seat `score` and `label`, and `metrics`; plus `costUsd` and
`costUnknownCalls` of this match.

### `match_aborted`

The match stopped before the game ended: `reason` (`cost_limit`, `timeout`, `player_failure`, `cancelled`, `internal_error` for an unexpected exception) and `detail`.
It can also appear after `match_finished` if the cost limit is reached or the run is cancelled during the debriefing; the result stays valid and the match directory still counts as a finished match with an aborted debriefing.

### `debrief`

The debriefing of one seat: `valid`, `answers` (all values as text: `3`, `true`, free text), `problems`, `attempts`.
The model calls of the debriefing are `model_call` events with `purpose: Debrief`.

## Cost figures

`model_call.call.costUsd` is the cost OpenRouter reported. If it reported none, `estimatedCostUsd` holds an estimate from tokens and the model price; the spending limit uses it, and `match.json` and the report add it to the cost and list how many calls were estimated. Calls with neither are `costUnknown` and are held back with a fixed reserve against the limit. `match_finished.costUsd` is the cost of the turns up to the end of the game; `match.json` totals all calls including the debriefing.

## match.json

Numbers only, computed from the protocol: game, date, duration, `aborted`, `outcome`, `resultWithRuleViolation`, and per seat the
player, requested and delivered model, score and label, turns, the counts described below, shortened thoughts and
messages, silences, tokens, cost and whether the debriefing was valid. Plus total cost, number of
calls, number of calls with unknown cost, version, commit and template hashes.

The counts of a seat come from one class (`PlayMetrics`), which the report, `match.json` and the series metrics of a game share:

| Key | Counts |
|---|---|
| `invalidAnswers`, `refusals`, `tokenLimitHits` | rejected answers, split by why they were rejected: not usable, declined to play, cut off by the output limit |
| `ruleViolations` | accepted moves that the rules forbid (`moveKind: RuleViolation`) |
| `resignations` | turns in which the seat gave the game up (`moveKind: Resignation`) |
| `exhaustedTurns` | turns that ended `exhausted`: no acceptable move in any attempt. Not a rule violation |

If the protocol has `oracle` facts, each seat also has two objects; without them (a game without an oracle, or a protocol written before
the oracle existed) both are `null`: not applicable.

| Key | Content |
|---|---|
| `strength.movesInWonPosition` | regular moves made in a won position (a resignation and a rule violation are not counted) |
| `strength.optimalMoves` | of those, the moves that kept the win |
| `strength.squanderedWins` | of those, the moves that gave the win up: the mistakes |
| `strength.firstMistakeTurn`, `strength.firstMistakeMove` | the turn id of the first mistake and its place among the turns of the seat (1 = first turn of the seat); `null` if there was none |
| `strength.mistakeRate` | `squanderedWins / movesInWonPosition`; `null` (not applicable) if the seat never moved in a won position, never 0 |
| `calibration.estimatesInWonPositions`, `calibration.meanEstimateInWonPositions` | number and mean of the estimates given in won positions; the mean is `null` without estimates |
| `calibration.estimatesInLostPositions`, `calibration.meanEstimateInLostPositions` | the same for lost positions |
| `calibration.brierCount`, `calibration.brier` | number and mean of the squared differences between estimate / 100 and the outcome of the position (1 won, 0 lost, 0.5 drawn; positions the oracle could not judge are left out); `null` without estimates |

`violationInLostPositionThenWon` is true if the seat broke a rule in a position that was lost for it and then won the game (it scored more
than its opponent), false otherwise, and `null` if it broke a rule but the protocol does not say in which position.

`resultWithRuleViolation` is true if the game ended and either seat made an accepted but forbidden move before the end. It is false for
a match that was aborted before a result. The mark exists only in `match.json` and in the report: it is never part of a prompt or of
the context of the debriefing, which sees the outcome and the labels of the game as the game words them. A protocol of version 1
has no kinds of move, so `ruleViolations`, `resignations` and `resultWithRuleViolation` are `null` in `match.json` and "n/a" in the workbook (not applicable, never 0), and the report leaves those rows out.

## report.md

Overview, result, history (with the analysis facts marked), the sections of the game's analyzer, **Play strength** and **Calibration** (only
if the protocol has `oracle` facts; Calibration only if some turn gave an estimate), **Rule compliance**, the private thoughts
kept apart, the figures per seat and the debriefing of both seats side by side. The section Rule compliance is the same for every game:
the rejected answers per player (invalid, refusals, cut off by the length limit), the rule violations per player with turn, move and the name of the
broken rule, resignations, and the exhausted turns **apart from** the violations. Next to the counted behavior it shows the answer to the
debriefing question `rules_followed` (only if the debriefing asked it), and it marks a result that was reached with a rule violation. With an
oracle it names the position before each violation and whether a seat broke a rule in a lost position and then won.

The section Play strength shows per player the moves in a won position, how many were optimal, the won positions given up, the turn of the
first mistake and the mistake rate. Resignations, rule violations and exhausted turns are never mistakes. The section Calibration shows the mean
estimate in won and in lost positions (each with its number) and the Brier score. A figure without a basis is shown as "n/a", never as 0.

## series.json

Numbers only, computed from the match protocols of the series: `gameId`, `playerA`, `playerB`, the number of matches,
completed and aborted matches, total cost with the number of estimated and unknown calls, `entries` and `players`.

- `entries`: one object per match with `directory`, `firstSeat`, `outcome` (absent for an aborted match) and `costUsd`.
- `players`: one object per player name that took part, over both seats, with `metrics`. It is empty unless the game declares
  series metrics (see [Adding a game](adding-a-game.md)). Every metric has `key`, `label`, `kind` (`Sum`, `Ratio` or `Mean`),
  `numerator`, `denominator` (not for a sum) and `value`. A ratio or a mean is the sum of all numerators divided by the sum of all
  denominators. `value` is always written; it is `null` ("n/a" in the report) if nothing could be measured, for example
  when the denominator is 0.

`series-report.md` shows the same: an overview, the list of matches with the starting seat, outcome and cost, and one table
per player that contains exactly the metrics of the game. A game without declared metrics has no table per player.
`shootout report <series directory>` rebuilds both files from the match directories.

## Reading a protocol

In C#:

```csharp
var match = MatchRecord.Load("matches/.../01-A-first");
foreach (var message in match.Messages(Seat.A)) Console.WriteLine(message.Text);
Console.WriteLine(match.Result?.Outcome);
```

On the command line (jq):

```bash
jq -r 'select(.type=="message") | "\(.seat): \(.text)"' events.jsonl
jq -s 'map(select(.type=="model_call") | .call.costUsd // 0) | add' events.jsonl
```
