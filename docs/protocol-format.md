# Protocol format

Every match writes a directory. The protocol is the single source of truth: reports, replays and repeated
debriefings are computed from it, and nothing computed later ever changes it.

```text
matches/2026-11-20_split-or-steal_gpt-oss-20b_vs_claude-haiku-5.5_01/
├─ events.jsonl                  the protocol, one JSON event per line, append-only
├─ match.json                    summary: result, key figures, cost, duration
├─ report.md                     readable report
├─ annotation.promise.<judge>.json   derived assessment by a judge model (optional, `shootout annotate`)
└─ debrief.<timestamp>.jsonl     answers of a repeated debriefing (optional, `shootout debrief`)
```

A series is a directory `..._series/` with one match directory per entry of the series file (`01-talk-A-first`,
`02-talk-B-first`, `03-silent-A-first`), plus `series.json` and `series-report.md`.

## Guarantees

- **Append-only and flushed after every event.** A match that is aborted (cost limit, timeout, failure, Ctrl+C)
  leaves a valid file that ends with a `match_aborted` event.
- **Raw data is kept.** The exact prompt, the unchanged answer, the reasoning text if the provider delivered one, the
  requested and delivered model, the OpenRouter generation id, tokens, cost, latency and the raw HTTP exchange (without headers, so without the API key).
- **Derived data lives elsewhere.** `report.md`, `match.json`, annotations and repeated debriefings are separate
  files. A test checks that `events.jsonl` stays byte-identical.
- **Readable.** Umlauts are written as they are (not as `ä`), so lines can be quoted.

## Versioning

Every line carries `"schemaVersion"`. The current version is **1**. The version is raised whenever the shape of an
event changes in a way that breaks readers (a field is renamed, removed, or changes its meaning). New optional
fields do not change the version. Readers must ignore unknown fields. `ProtocolVersion.Current` is the constant in code.

Naming: property names are camelCase; enum values are written by name (`Turn`, `Everyone`, `A`); keys of
dictionaries are written unchanged (`A`, `B`, `trust_in_opponent`, `split-or-steal`).

## Events

All events have `type`, `at` (UTC timestamp) and `schemaVersion`.

### `match_started`

First line. Everything needed to understand and repeat the match.

| Field | Meaning |
|---|---|
| `gameId`, `seed`, `language`, `communication`, `firstSeat` | the game and its setup |
| `gameOptions` | game specific options from the series file, or absent |
| `players` | per seat: `name` (for example `openrouter:openai/gpt-oss-20b`, `ollama:llama3.2` or `random`; the prefix is the [endpoint](endpoints.md)), `requestedModel`, and `settings` (temperature, reasoning effort, output limit, call timeout, only those that are sent; a repeated debriefing uses them again) |
| `templateHashes` | hash of the template file of the arena and of the game, for the language used |
| `options` | the `MatchOptions`: attempts, word limits, thought memory, identity settings, cost limit, refusal keywords |
| `appVersion`, `gitCommit` | version and short commit of the application |

### `model_call`

One call to a player: a turn, a debriefing, or (in the annotation file) a judge.

| Field | Meaning |
|---|---|
| `turnId`, `purpose` (`Turn`, `Debrief`, `Judge`), `seat`, `attempt` | which call |
| `messages` | the complete prompt as sent: `role` and `text` of every message |
| `rawContent`, `rawReasoning` | the unchanged answer and the reasoning channel |
| `parsed` | `thought`, `message`, `move`, `fromReasoning`, `extraKeys` as read by the arena (turns only) |
| `class` | `valid`, `invalid`, `refusal`, `token_limit`, `technical_error` |
| `violations` | what was wrong, one line per field |
| `call` | `requestedModel`, `deliveredModel`, `provider`, `generationId`, `promptTokens`, `completionTokens`, `reasoningTokens`, `costUsd`, `estimatedCostUsd`, `latency`, `finishReason`, `httpAttempts` |
| `costUnknown` | true if the endpoint reported no cost and none could be estimated (the call is held back with a reserve) |
| `error` | text of a technical error |
| `rawExchange` | request and response body of the last HTTP attempt, without headers |

```json
{"type":"model_call","turnId":"talk-1","purpose":"Turn","seat":"A","attempt":1,"messages":[{"role":"System","text":"..."},{"role":"User","text":"..."}],"rawContent":"{\"thought\":\"...\",\"message\":\"Let us split.\",\"move\":null}","parsed":{"thought":"...","message":"Let us split.","fromReasoning":false,"extraKeys":[]},"class":"valid","violations":[],"costUnknown":false,"at":"2026-11-06T10:14:10.3674841+00:00","schemaVersion":1}
```

### `message`

A public message became visible. `text` is absent for silence.

```json
{"type":"message","turnId":"talk-1","seat":"A","text":"Let us split.","silent":false,"truncated":false,"identityHits":[],"at":"...","schemaVersion":1}
```

`identityHits` entries look like `opponent:Provider:ChatGPT` (`self`, `opponent` or `ambiguous`; the marker type comes from the scanner).

### `turn`

The final outcome of a turn of a seat: the move, the status (`played` or `exhausted`), the private `thought`, and
counters: `attempts`, `invalidAnswers`, `refusals`, `tokenLimitHits`.

### `fact`

Something the game reports. `audience` is `Everyone`, `Seat` (only the named seat sees it in a prompt) or
`Analysis` (never in a prompt, for example a Nim oracle verdict). `text` is the neutral wording ("Seat A"),
`template` the key and values so that it can be reworded for a viewer, `data` machine readable details. Facts of a round are filed under the `turnId` of the turn of their seat; hidden starting state (dice, a drawn code) comes from `IGame.Start()` and has `turnId` `start`.

```json
{"type":"fact","turnId":"decide","kind":"decision","audience":"Everyone","seat":"A","text":"Decision (Seat A): split","template":{"key":"fact.decision","values":{"decision":"split"}},"data":{"decision":"split"},"at":"...","schemaVersion":1}
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

Numbers only, computed from the protocol: game, date, duration, `aborted`, `outcome`, and per seat the player, requested
and delivered model, score and label, turns, invalid answers, refusals, token-limit hits, shortened thoughts and
messages, silences, identity hits, tokens, cost and whether the debriefing was valid. Plus total cost, number of
calls, number of calls with unknown cost, version, commit and template hashes.

## Annotation files

`annotation.<task>.<judge>.json` contains the judge's name, a note that this is a derived assessment, the time, and
one entry per question: `key`, `seat`, the parsed `result`, the unchanged `raw` answer and an `error` if the answer
was unusable. The Split or Steal report reads `announced` from it.

## Reading a protocol

In C#:

```csharp
var match = MatchRecord.Load("matches/.../01-talk-A-first");
foreach (var message in match.Messages(Seat.A)) Console.WriteLine(message.Text);
Console.WriteLine(match.Result?.Outcome);
```

On the command line (jq):

```bash
jq -r 'select(.type=="message") | "\(.seat): \(.text)"' events.jsonl
jq -s 'map(select(.type=="model_call") | .call.costUsd // 0) | add' events.jsonl
```
