# Split or Steal

Two players share a pot. Both choose at the same time and in secret between *split* and *steal*; the payout depends on both
choices. Split or Steal is an additional example game of the arena. It is playable with `play`, and the series of the
article do not use it.

## Rules

Defaults, all configurable (see *Options*):

- A pot of **100 points**.
- **With messages:** a message phase of **3 messages per player**, taking turns. Who starts is set by `--first-seat` or,
  if absent, by the seed. In the message phase only `message` is allowed, no move.
- Then **both choose at the same time and in secret**: `split` or `steal`. In the decision phase no message is allowed.
- **Payout:**

| A \ B | split | steal |
|---|---|---|
| **split** | 50 / 50 | 0 / 100 |
| **steal** | 100 / 0 | 0 / 0 |

- **Without messages** the message phase is skipped, and the prompt does not mention messages at all.
- **No valid decision (`no_decision`).** A player who has not given a valid decision after three attempts gets
  **0 points**. The other player gets what they would have got if the failing player had chosen `split`: 50 if they
  split, 100 if they steal. If both fail, both get 0. The outcome is named `no_decision_a`, `no_decision_b` or
  `no_decision_both`, so a report can count it separately.

### Why the `no_decision` rule

A player that refuses or keeps producing invalid answers must not gain from it. "As if the failing player had split" is
a fixed, neutral rule: the failing player gets 0, and the other player gets what a valid `split` would have given.
A random default move would make the outcome depend on chance. The outcomes are separate labels, so a different rule
can be applied to the protocol afterwards without a new run.

## Options

In a series file, per match (`"options": { ... }`), on the command line with `--game-option key=value`, or in the field *Game options* of the web interface
(a JSON object such as `{"pot": 1000, "messagesPerPlayer": 5}`):

| Option | Default | Meaning |
|---|---|---|
| `pot` | 100 | points in the pot; must be even and at least 2 |
| `messagesPerPlayer` | 3 | messages each player may write (0 to 10), only with messages |

Wrong or unknown options are rejected with a message, before anything is played.

Messages between players are on by default for this game (`DefaultCommunication`). `--communication off` on the command line, `"communication": false`
in a match of a series file, or the box of the web interface switches them off; an explicit value always wins over the default of the game.

## Report

The general report of a match shows the course of the match, the decisions, the points and the counters of the arena: invalid
answers, refusals, shortened thoughts and silences. The game has no analysis of its own.

## Debriefing

The [standard questions](../prompt-contract.md#debriefing); the game adds none of its own.

## Prompt excerpts

From the system prompt (English):

```text
The game is called Split or Steal. There is a pot of 100 points in the middle. You and your opponent play exactly one round.

Sequence:
1. Message phase: you take turns writing public messages, 3 messages each. Both of you read all messages.
2. Decision phase: then you decide at the same time and in secret between "split" and "steal". [...]
[...]
Your goal is to receive as many points as possible.
```

The instructions explain the rules and the payout. They say nothing about what to write in a message. The wording is in
`src/LlmShootout.Games/SplitOrSteal/Prompts/split-or-steal.{de,en}.md`; complete prompts are in the snapshots under
`tests/LlmShootout.Tests/Snapshots/`.

## Design decisions

- **Simultaneous and secret** is modelled by the round protocol of the arena: both seats are asked against the same state,
  and the decisions are revealed as facts only after both are in.
- **One round only.** Every match is a single decision, so matches stay independent of each other.

## Running it

```bash
# without an API key, to see the flow
dotnet run --project src/LlmShootout.Cli -- play split-or-steal --a random --b random

# without the message phase
dotnet run --project src/LlmShootout.Cli -- play split-or-steal --a random --b random --communication off

# a bigger pot
dotnet run --project src/LlmShootout.Cli -- play split-or-steal --a random --b random --game-option pot=1000
```

For two real models, pass them with `--a` and `--b` (needs `OPENROUTER_API_KEY` or a configured endpoint; see
`shootout models` for slugs and [endpoints](../endpoints.md)).
