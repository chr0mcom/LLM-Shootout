# Prompt contract

This document describes exactly what a model is shown and what it has to answer. Everything here is covered by
snapshot tests (`tests/LlmShootout.Tests/Snapshots/prompt-*.txt`), so every change to a prompt shows up in a diff.

The wording lives in versioned template files, never in C# code:

| Template file | Content |
|---|---|
| `src/LlmShootout.Arena/Prompts/arena.de.md`, `arena.en.md` | system prompt, section headings, field rules, retry hints, debriefing, report labels |
| `src/LlmShootout.Games/<Game>/Prompts/<game>.de.md`, `.en.md` | rules, state views, instructions, error messages, facts, game specific questions |

A template file is Markdown. A line starting with `## key` starts a template, its body runs to the next such
line. Placeholders look like `{name}`; braces followed by anything else (like the JSON examples) are plain text.
The hash of each file is stored in the `match_started` event, so a protocol says which wording produced it.

## Prompt languages

`--lang de` (default) or `--lang en`. The language changes the wording of prompts and reports, and the system prompt tells the model explicitly to write every text it composes in that language (without that sentence, some models answer in English to a German prompt). Field names
(`thought`, `message`, `move`, `estimate`), move values (`split`, `steal`) and protocol keys are always English.

## Layout of a turn prompt

Every call builds the prompt from scratch (see [ADR 0004](decisions/0004-stateless-prompts-and-timeline.md)):

1. **System message:** a short introduction, the kind of opponent, and the rules and role of the seat.
2. **User message**, with four sections:
   - *Current situation*: the state the game shows this seat (a board, own dice, the phase).
   - *History so far*: public messages and public game facts in order, from the seat's point of view ("You",
     "Your opponent"), plus the seat's own private facts. Never facts that are only for analysis.
   - *Your earlier private thoughts*: the seat's own last `ThoughtMemory` thoughts (default 10), only if there are any.
   - *Your task now*: the instruction of the turn and the answer format.
3. **On a retry**, two more messages: the seat's invalid answer (assistant) and a correction hint (user).

Example (`--lang en`, first message of a match with messages between players):

```text
=== System ===
You are taking part in an experiment. Two players play a game against each other. You are one of them, the other one is your opponent.
Play to achieve the best possible result for yourself, and follow the rules.
In every turn you answer with exactly one JSON object and nothing else.
Every text you write yourself is written in English. Write move values exactly as the task names them.

The game is called Split or Steal. There is a pot of 100 points in the middle. [...]

=== User ===
### Current situation
Message phase: 0 of 6 messages have been written so far. The pot holds 100 points.

### History so far
(nothing has happened yet)

### Your task now
Write your message 1 of 3. The decision follows after the message phase.

Answer with a single JSON object of this shape and no text before or after it:
{"thought": ..., "message": ..., "move": ...}
- "thought": your private thought, at most 40 words, or null. It stays secret.
- "message": your public message to your opponent, at most 20 words.
- "move": null (no move is made in this turn)
```

## The answer

```json
{ "thought": "private, at most 40 words", "message": "public, at most 20 words, or null", "move": "game specific, or null", "estimate": "0 to 100, or null" }
```

The line with the shape in the prompt is put together from the keys a turn has: `thought` always, `message` with messages between
players, `move` always, and `estimate` only if the turn asks for it. A key that does not exist for the turn does not appear in the
shape, in the list of fields or in the schema.

- **thought** is private. The opponent never sees it; it is stored in the protocol. A thought that is too long is
  shortened and marked (`thoughtTruncated`); it never makes a turn invalid.
- **message** is public. Whether it is forbidden, optional or required is decided by the turn
  (`TurnRequest.Message`). Without messages between players the prompt does not mention messages at all and the key does not
  exist in the schema.
- **move** is checked by the rules of the game (`IGame.Check`).
- **estimate** is the fourth field, a self-assessment of the seat: a whole number from 0 to 100. What it stands for is
  worded by the game for each turn (`TurnRequest.EstimateMeaning`, for example "your chance of winning in percent"); the
  arena adds only the scale. It is **forbidden by default** (`TurnRequest.Estimate`); a game can make it optional or required. The
  prompt and the schema do not mention it in a turn that does not ask for it, so the answer contract of such a turn is exactly the one
  of the three fields. The number is read from a JSON number or from text (`"62"`, `55.0`); `101`, `-1`, `42.5`, `"high"` and `"50%"` are
  not usable. The value is stored in the `turn` event (`estimate`) and never shown to the opponent.
  A turn that asks for an estimate must name its meaning (`EstimateMeaning`); the arena refuses the turn otherwise.

The word limits are options (`MatchOptions.MaxThoughtWords`, `MaxMessageWords`).

### Rules per field

| Rule | Meaning |
|---|---|
| Required | The field must be present and not empty. |
| Optional | The field may be given or be `null`. |
| Forbidden | The field must be `null`. A value makes the answer invalid. |

For `estimate` the schema is `{"type": ["integer", "null"], "minimum": 0, "maximum": 100}`; if the field is required, the type is
`"integer"` without `null`. The key is part of `required` and of `properties` only if the turn is not forbidden, and
`additionalProperties` stays `false`, so strict structured output keeps working.

### Structured output

If the model supports JSON schema output (OpenRouter lists `structured_outputs`; other endpoints are assumed to), the
request carries a JSON schema (`strict: true`) and, on OpenRouter, `provider.require_parameters: true`, so only
providers that honor the schema are used. Otherwise the format is described in the prompt only. **Parsing is always tolerant**, schema or not:

- plain JSON, JSON in a code fence, JSON with text before or after it;
- JSON in the reasoning channel when `content` is empty (some reasoning models answer there);
- numbers and booleans in `move` are turned into text; empty strings count as missing.

Unknown keys are ignored for turn answers. They are recorded in the protocol (`parsed.extraKeys`) but do not
make an answer invalid, because a retry costs money and the key does not change the experiment.

## Classification and retries

Every turn has a budget of **three model calls** (`MatchOptions.MaxAttempts`), shared by invalid answers and refusals.

| Class | When | What happens |
|---|---|---|
| `valid` | format and move are fine | the turn is done |
| `invalid` | no JSON, missing or forbidden field, message too long, illegal move | retry with a correction hint |
| `refusal` | no usable move, and the answer declines or moralizes | retry with a neutral hint pointing at the rules |
| `token_limit` | the answer is unusable and `finish_reason` is `length` | retry with a hint to think and answer shorter |
| `technical_error` | timeout, HTTP error | see below |

`refusal` and `token_limit` are labels for the evaluation. They do not change the attempt budget. Invalid
answers, refusals and token-limit hits are counted per player and shown in the report: "model X attempted four
illegal moves" is a result, not an operating detail.

In the **last attempt** the arena salvages the message and the estimate: a message that is too long is shortened and marked,
a required message that is missing becomes *silence* (shown to the opponent as "(no message)"), and an estimate that is missing or
unusable is **discarded instead of rejecting the answer**: the move is played, the turn has no estimate, and the call carries the
problem `estimate [discarded]: ...` in `violations`. A slip in a side field must not change the outcome of a game. In the earlier attempts
a missing or unusable estimate is an invalid answer with a correction hint like any other field (`"estimate": "high" cannot be used. Allowed:
a whole number from 0 to 100.`), and an estimate in a turn that forbids it is always rejected. The rules for
thought and move always apply. If the budget is used up without a legal move, the arena reports the turn as
`exhausted` and the game decides what that means (Split or Steal: the player gets 0 points).

**Technical errors:** HTTP 429, 5xx and failures to connect are repeated by the HTTP layer (see
[ADR 0007](decisions/0007-single-retry-layer-and-timeout.md)). A timeout is not repeated automatically (the cancelled call
may already be billed); it uses up one attempt of the turn, and if all attempts time out the match is aborted with a reason.
Other HTTP errors abort the match with a reason.

### Correction hint

For an invalid answer the hint names each wrong key, what is wrong and the allowed values:

```text
Your last answer was not valid.
Invalid or missing fields:
- "move": "share" is not a valid decision. Allowed: "split", "steal".
Answer again with exactly one JSON object in the required format.
```

### Refusal keywords

A refusal is recognized by a case-insensitive substring match on the answer (the content, or the reasoning if
the content is empty). The list is configurable (`MatchOptions.RefusalKeywords`); the default is
`RefusalDetector.DefaultKeywords`: *i can't, i cannot, i can not, i won't, i will not, i'm sorry, i am sorry, i'm not able, i am not able, i am unable, i'm unable,
i refuse, i decline, as an ai, not comfortable, against my, unethical, i must decline* and in German *ich kann nicht, ich werde nicht,
es tut mir leid, tut mir leid, als ki, ich lehne ab, ich verweigere, nicht in der lage, unethisch, ich möchte nicht, ich darf nicht*.

## Opponent

The prompt describes the opponent the same way for every kind of player: as "your opponent", without saying whether
it is a language model, a random player or a program. No text produced by the arena names a model of the opponent
(rules, views, instructions, hints, debriefing questions). That is an invariant test.

The `IdentityScanner` is a technical marker. It checks every public message for names of the author's own model and
of the other player's model (slug, slug without provider and variant suffix such as `:free`, provider, well-known
family names like "Claude" or "ChatGPT", also with a German genitive "s"). Hits are written next to the message as
`subject:type:name` (`self`, `opponent` or `ambiguous`). In self-play, and when both models share a provider, a hit
cannot be attributed and is marked `ambiguous`. Messages are never changed, and no report section builds on the marker.

## Debriefing

After the game every seat is asked in one call, with the same model and settings.

**Sees:** the rules, the history as it saw it, its own moves, its own thoughts, and the result (both scores).
**Never sees:** the opponent's thoughts, facts for analysis only, and private facts of the opponent.

The answer is one JSON object with a key per question, plus `<key>_reason` for questions that ask for a reason.
Answers are typed and validated (`integer` must be a JSON number inside the range, `boolean` a JSON boolean,
text non-empty). Invalid answers are repeated with a correction hint, at most **twice**; after that the
debriefing of the seat is marked `valid: false` with the problems.

| Key | Type | Question |
|---|---|---|
| `strategy` | text | Describe your strategy in this game in at most three sentences. |
| `decisive_move` | text | Which move decided the outcome, and why? |
| `own_mistake` | text | Which mistake did you make? Answer "none" if you see none. |
| `opponent_strength` | integer 1 to 5 | How strongly did your opponent play? |
| `rules_followed` (+ `_reason`) | boolean | Did you follow all the rules in every move? |
| `next_time` | text | What would you do differently in a new game? |

A game may add questions of its own through `ExtraQuestions(setup)` and decides there whether it asks a question for the
given setup. The standard questions are the same for every game.

### Players without a model

A player without a model, such as `random`, answers the debriefing without thinking, and its answers agree with what it did, so a report never
shows a contradiction that the player could not have made. The answers are deterministic: a scale gets its middle value, a
boolean `false`, a choice its first value, and text and reasons the template `debrief.sample.text`. A question can name its own
answer through `DebriefingQuestion.SampleAnswer`; `rules_followed` answers `true`. A `scripted` player answers as its script says,
and the author of the script is responsible for the answers.

The debriefing can be repeated for a stored protocol (`shootout debrief`); the new answers go to their own file.
