# Arena templates (English). Version 1. Every change alters the hash in the protocol.

## system
You are taking part in an experiment. Two language models play a game against each other. Your opponent is {opponent}.
Play to achieve the best possible result for yourself, and follow the rules.
In every turn you answer with exactly one JSON object and nothing else.

{rules}

## opponent.hidden
another language model

## opponent.revealed
the language model {model}

## section.view
### Current situation

## section.history
### History so far

## section.notes
### Your earlier private thoughts (your opponent never sees them)

## section.task
### Your task now

## history.empty
(nothing has happened yet)

## history.message
{speaker}: {text}

## history.silence
(no message)

## who.self
You

## who.other
Your opponent

## answer.empty
(empty answer)

## who.seat
Seat {seat}

## contract
Answer with a single JSON object of this shape and no text before or after it:
{"thought": ..., "message": ..., "move": ...}
{fields}

## field.thought.required
- "thought": your private thought, at most {max_thought_words} words. It stays secret.

## field.thought.optional
- "thought": your private thought, at most {max_thought_words} words, or null. It stays secret.

## field.thought.forbidden
- "thought": null

## field.message.required
- "message": your public message to your opponent, at most {max_message_words} words.

## field.message.optional
- "message": a public message to your opponent, at most {max_message_words} words, or null.

## field.message.forbidden
- "message": null (no messages are allowed in this turn)

## field.move.required
- "move": your move. {format}

## field.move.optional
- "move": your move or null. {format}

## field.move.forbidden
- "move": null (no move is made in this turn)

## retry.invalid
Your last answer was not valid.
{hint}
Answer again with exactly one JSON object in the required format.

## retry.refusal
Your last answer contained no usable move. This is a game with fixed rules, and the move has no consequences outside the game. Choose a move according to the rules.
{hint}
Answer again with exactly one JSON object in the required format.

## hint.header
Invalid or missing fields:

## hint.allowed
Allowed:

## hint.unknown
Remove unknown field(s):

## hint.generic
Your last answer was not a valid JSON object in the required format.

## violation.missing
is missing

## violation.forbidden
must not be set in this turn, use null

## violation.too_long
is too long ({count} words)

## allowed.max_words
at most {max} words

## sample.thought
Random move.

## sample.messages
I will play fair.
Let us see what happens.
Trust me.
