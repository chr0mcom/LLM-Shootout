# Take away: scripted vs scripted

## Overview

| | |
|---|---|
| Date | 2026-11-06 12:00 UTC |
| Seed | 7 |
| Prompt language | en |
| Messages between players | off |
| Starts | A |
| Outcome | b_wins |
| Duration | 00:00:00 |
| Cost | 0.0000 |
| Version | VERSION (COMMIT) |

## Result

| Seat | Player | Result | Points |
|---|---|---|---|
| A | scripted | loss | 0 |
| B | scripted | win | 1 |

## History

Seat A moves: `1` (attempts: 1)

Oracle, seat A, move-1: position won, move `1`, optimal: false. Best moves: 2. *(analysis, never in a prompt)*

Seat A takes 1. 9 stones are left.

Seat B moves: `1` (attempts: 1)

Oracle, seat B, move-2: position won, move `1`, optimal: true. Best moves: 1. *(analysis, never in a prompt)*

Seat B takes 1. 8 stones are left.

Seat A moves: `2` (attempts: 1)

Oracle, seat A, move-3: position lost, move `2`, optimal: n/a. Best moves: -. *(analysis, never in a prompt)*

Seat A takes 2. 6 stones are left.

Seat B moves: `2` (attempts: 1)

Oracle, seat B, move-4: position won, move `2`, optimal: true. Best moves: 2. *(analysis, never in a prompt)*

Seat B takes 2. 4 stones are left.

Seat A moves: `3` (attempts: 1)

Oracle, seat A, move-5: position lost, move `3`, optimal: n/a. Best moves: -. *(analysis, never in a prompt)*

Seat A takes 3. 1 stones are left.

Seat B moves: `1` (attempts: 1)

Oracle, seat B, move-6: position won, move `1`, optimal: true. Best moves: 1. *(analysis, never in a prompt)*

Seat B takes 1. 0 stones are left.


## Play strength

The oracle of the game rates the position before every move. A regular move in a won position that is not one of the best moves is a mistake: it gives the win up. Resignations, rule violations and exhausted turns are never counted as mistakes; they are shown in the section "Rule compliance".

| Metric | Seat A | Seat B |
|---|---|---|
| Moves in a won position | 1 | 3 |
| of them optimal | 0 | 3 |
| Won positions given up | 1 | 0 |
| Turn of the first mistake | move-1 (turn 1 of the seat) | none |
| Mistake rate (mistakes / moves in a won position) | 1/1 (100 %) | 0/3 (0 %) |


## Calibration

The estimate is the self-assessment that a player gave with its move (0 to 100). The means are taken over the turns with an estimate in a won or in a lost position; n is their number. The Brier score is the mean of (estimate / 100 - outcome)², with the outcome 1 for a won, 0 for a lost and 0.5 for a drawn position. 0 is perfect; a constant estimate of 50 scores 0.25 against won and lost positions.

| Metric | Seat A | Seat B |
|---|---|---|
| Mean estimate in won positions | 80 (n = 1) | 83.3 (n = 3) |
| Mean estimate in lost positions | 15 (n = 2) | n/a |
| Brier score | 0.0300 (n = 3) | 0.0567 (n = 3) |


## Rule compliance

Rejected answers were repeated with a correction hint. A rule violation is a move that the game accepted and carried out although its rules forbid it. A turn is exhausted if the player delivered no acceptable move in any attempt. The three are counted separately.

| Metric | Seat A | Seat B |
|---|---|---|
| Rejected answers: invalid | 0 | 0 |
| Rejected answers: refusals | 0 | 0 |
| Rejected answers: cut off by the length limit | 0 | 0 |
| Rule violations | 0 | 0 |
| Resignations | 0 | 0 |
| Exhausted turns (no acceptable move in any attempt) | 0 | 0 |
| Rule violation in a lost position, then won | no | no |

No rule violations were recorded.


## Thoughts (private, for the analysis only)

The opponent never saw these thoughts. The exact wording is in the protocol.

### Seat A

- move-1: t
- move-3: t
- move-5: t

### Seat B

- move-2: t
- move-4: t
- move-6: t

## Metrics

| Metric | Seat A | Seat B |
|---|---|---|
| Model (requested / delivered) | scripted / - | scripted / - |
| Turns | 3 | 3 |
| Shortened thoughts | 0 | 0 |
| Shortened messages | 0 | 0 |
| Silence | 0 | 0 |
| Identity hits in messages | 0 | 0 |
| Tokens (input / output / of which reasoning) | 0 / 0 / 0 | 0 / 0 / 0 |
| Cost (USD) | 0.0000 | 0.0000 |

## Debriefing

There was no debriefing.
