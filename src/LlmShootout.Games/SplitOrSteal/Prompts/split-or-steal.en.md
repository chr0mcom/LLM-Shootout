# Split or Steal (English). Every change alters the hash in the protocol.

## rules.talk
The game is called Split or Steal. There is a pot of {pot} points in the middle. You and your opponent play exactly one round.

Sequence:
1. Talking phase: you take turns writing public messages to each other, {messages} messages each. Both of you read all messages.
2. Decision phase: then you decide at the same time and in secret between "split" and "steal". Nobody sees the other's decision before both have decided.

Payout:
- Both choose split: each receives {half} points.
- One chooses steal, the other split: whoever chose steal receives all {pot} points, the other 0.
- Both choose steal: both receive 0 points.

Whoever fails to give a valid decision receives 0 points. The other then receives what they would get if the failing player had chosen split.

Your goal is to receive as many points as possible.

## rules.silent
The game is called Split or Steal. There is a pot of {pot} points in the middle. You and your opponent play exactly one round.

You decide at the same time and in secret between "split" and "steal". Nobody sees the other's decision before both have decided. You cannot communicate.

Payout:
- Both choose split: each receives {half} points.
- One chooses steal, the other split: whoever chose steal receives all {pot} points, the other 0.
- Both choose steal: both receive 0 points.

Whoever fails to give a valid decision receives 0 points. The other then receives what they would get if the failing player had chosen split.

Your goal is to receive as many points as possible.

## view.talk
Talking phase: {done} of {total} messages have been written so far. The pot holds {pot} points.

## view.decide
Decision phase. The pot holds {pot} points.

## instruction.talk
Write your message {own} of {messages} to your opponent. You decide only after the talking phase.

## instruction.decide
Decide now: split or steal. Your opponent decides at the same time and in secret.

## format.decide
Exactly one of "split" or "steal".

## error.move
"{move}" is not a valid decision

## fact.decision
Decision ({who}): {decision}

## fact.no_decision
Decision ({who}): no valid decision

## fact.payout
Payout ({who}): {points} points
