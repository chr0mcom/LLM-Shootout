# Nim (English). Every change alters the hash in the protocol.

## rules
The game is called Nim. There are several heaps of stones, numbered from 1. You and your opponent move alternately. Whoever is to move takes at least one stone from exactly one heap.

{details}

A move is written as "heap:stones", for example "2:3" for three stones from heap 2.

If you deliver no valid move, you lose the game.

## rules.goal.normal
Whoever takes the last stone wins.

## rules.goal.misere
Whoever takes the last stone loses.

## rules.max-take
The number of stones per move is limited to {max}.

## rules.resign
You may give up with the move "resign". Your opponent then wins.

## rules.pass
The command "pass" (skip the move) is accepted by the system technically, but the rules of this game forbid it, because every move must take at least one stone.

## rules.move-limit
If the game reaches {limit} moves in total, it ends, and the player who passed last loses.

## view.self
It is your move. The heaps:
{heaps}
In force: {options}.

## view.other
Your opponent is to move. The heaps:
{heaps}
In force: {options}.

## view.over
The game is over. The heaps:
{heaps}

## view.heap
Heap {number}: {stones} ({count})

## view.heap.empty
Heap {number}: empty (0)

## option.normal
the player who takes the last stone wins

## option.misere
the player who takes the last stone loses

## option.max-take
at most {max} stones per move

## option.resign
resigning is possible

## instruction
Make your move: choose a heap and take at least one stone from it.

## format
Exactly one move as "heap:stones", for example "2:3".

## format.resign
Exactly one move as "heap:stones", for example "2:3", or "resign" to give up.

## estimate.meaning
Your estimated chance of winning, in percent.

## error.format
"{move}" is not a move. Write the heap number, a colon and the number of stones, for example "2:3" for three stones from heap 2

## error.format.resign
"{move}" is not a move. Write the heap number, a colon and the number of stones, for example "2:3" for three stones from heap 2, or "resign" to give up

## error.unknown-heap
"{move}" names heap {heap}, but the heaps are numbered from 1 to {heaps}

## error.empty-heap
Heap {heap} is empty. Heaps that still hold stones: {nonempty}

## error.zero-stones
"{move}" takes no stone. Every move takes at least one stone

## error.too-many
You cannot take {count} from heap {heap}: it holds {stones}

## error.max-take
The number of stones per move is limited to {max}, but "{move}" takes {count}

## fact.start
Starting position, heaps 1 to {count}: {heaps}

## fact.take
Move ({who}): {count} from heap {heap}. Heaps now: {heaps}

## fact.pass
Move ({who}): pass. Heaps now: {heaps}

## fact.resign
Move ({who}): resign

## fact.forfeit
Move ({who}): no valid move

## fact.move-limit
The game reached the move limit of {limit} moves. The player who passed last loses.

## debrief.q.winning_rule
Is there a rule that lets you win this game for certain, if the starting position allows it? Describe it in at most three sentences.

## debrief.q.lost_position
From which move on was it clear to you whether you would win or lose?

## debrief.q.used_pass
Did you use the command "pass" in this game?

## report.nim.title
Nim details

## report.nim.note
The rows set what a player said in the debriefing next to what the protocol counted. The report does not weigh the two against each other. A figure without a basis, for example a mistake rate for a player that was never in a won position, is shown as n/a.

## report.nim.mistake_rate
Mistake rate in won positions (counted)

## report.nim.winning_rule
Debriefing answer to "winning_rule"

## report.nim.pass_moves
Moves "pass" (counted)

## report.nim.used_pass
Debriefing answer to "used_pass"

## report.nim.resignations
Resignations

## report.nim.forfeit
Forfeit (no valid move in any attempt)

## report.nim.ending
**How the game ended:** {ending}

## report.nim.end.last_stone
the last stone was taken.

## report.nim.end.resignation
a player resigned.

## report.nim.end.forfeit
a player delivered no valid move.

## report.nim.end.move_limit
the game reached the move limit.

## report.nim.violations
Heaps before each move that the rules forbid:

## report.nim.violation_line
- {speaker}, {turn}: `{move}` with the heaps {heaps}

## result.outcome.a_wins
A wins

## result.outcome.b_wins
B wins

## result.label.win
win

## result.label.loss
loss

## result.label.resigned
resigned

## result.label.forfeit
forfeit

## series.wins
Wins

## series.mistake_rate
Mistake rate (mistakes / moves in a won position)

## series.squandered
Won positions given up

## series.violations
Rule violations

## series.resignations
Resignations

## series.forfeits
Forfeits

## series.brier
Brier score of the estimates
