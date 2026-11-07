# 0022 - Nim: the oracle, the optimal player's rule, the debriefing questions, the analyzer and the series metrics

Status: accepted (2026-11-07)

## Context
The arena can rate every move of a game that has an oracle ([ADR 0019](0019-oracle-rates-every-move.md)), offers the player type `optimal`
for such games ([ADR 0020](0020-optimal-player-and-oracle-capability.md)) and summarizes a series with the metrics that a game declares
([ADR 0014](0014-series-report-summarizes-declared-metrics.md)). Nim ([ADR 0021](0021-nim-options-move-format-and-endings.md)) has to provide the oracle,
the questions that put the answers of a model next to its behavior, and the numbers of the series report. The results must be exact, checkable
against independent references, and explained in a way a reader of the article can follow.

## Decision
- **The oracle is a minimax search with a memo** (`NimOracle`). `Wins(position)` is true if a regular move (heap h, n stones with
  n up to the smaller of the heap and `maxTake`) leads to a position that is not won for the opponent. With no stone left, the player to move
  has lost in normal play (the opponent took the last stone) and has won in misère play. The same search covers normal play, misère, a limit per
  move and every combination; no formula is used in the product code. The value of a position does not depend on the order of the heaps, so the memo
  is keyed by the sorted non-empty heaps, four bits each (a heap has at most 15 stones). Five heaps give at most 15,504 positions, so the search is
  instant. The memo belongs to the two options `misere` and `maxTake` only, so `NimOracle.For(misere, maxTake)` hands out one shared, thread-safe
  instance per pair (the web interface plays several matches at once). The list of moves is `NimMove.Legal`, the one used by the game itself.
- **The oracle rates regular play only**: no `pass`, no `resign`, no move limit. `NimGame` implements `IGameOracle`. `Assess` returns `Won` with all
  moves into a position that is lost for the opponent, or `Lost` without best moves. The moves are `h:n` in the order of heap and count, and the heap is the
  real index, also if several heaps are equally large (`[3,3,1]` has the best moves `1:1`, `2:1` and `3:1`). `Unknown` and `Drawn` do not occur in Nim.
- **`optimal` plays the best moves and, in a lost position, one fixed move.** In a won position `ChooseMove` takes one of the best moves with the seeded
  generator of the player, so a match can be repeated. In a lost position it takes one stone from the largest heap and, if several heaps are equally
  large, from the one with the smallest number. The rule never uses the generator and always names a regular move, because `maxTake` is at least 1 and
  the largest heap of an unfinished game is not empty. The player never passes, never resigns and never gives up.
- **The tests compare the oracle with references that the oracle does not use.**
  - Normal play without a limit: lost exactly at XOR 0, for all ordered positions of up to 5 heaps with 0 to 7 stones.
  - Misère without a limit: if no heap has more than one stone, the player to move loses exactly with an odd number of heaps of one; otherwise exactly at XOR 0.
  - Normal play with a limit k: the Grundy value of a heap is `n mod (k+1)`, lost exactly at XOR 0 of these values.
  - Every variant, including misère with a limit (which has no simple formula), against `NimBruteForce`, a separate recursion over lists without a
    memo, without sorting and without a formula, for all positions of up to 3 heaps with up to 5 stones. The best moves are compared with its set as well.
  - Property test: over 500 seeds, both seats starting, all variants (normal and misère, no limit, 2 and 3, with and without temptation and
    resignation), `optimal` never gives a won position up against `random` and wins every game in which it is ever in a won position. In a lost position
    it plays the documented move. It plays no `pass` and no `resign`. Two optimal players finish every variant, and the player to move wins exactly if the start is won.
- **Debriefing questions.** `NimDefinition.ExtraQuestions` adds `winning_rule` (text) and `lost_position` (text) to the standard questions, and with
  `temptation: "pass"` also `used_pass` (true or false with a reason). The wording of the question about the winning rule says "a rule that lets you win
  this game for certain, if the starting position allows it" and does not hint at a method. The question about `pass` names the command only because the
  rules name it already, and it does not call it forbidden or a violation. The sample answer of a player without a model is `false`
  ([ADR 0013](0013-standard-debriefing-and-consistent-sample-answers.md)), so `random` and `optimal` do not contradict their behavior. Snapshot tests fix
  the debriefing prompt in both languages, with and without the temptation. Further tests assert that no analysis fact (the ratings of the oracle, the
  record of a rule violation), no mark "result reached with a rule violation" and no statement of the winning strategy appears in any prompt or debriefing.
- **The analyzer adds only what is specific to Nim** (`NimAnalyzer`). The arena already reports play strength, calibration and rule compliance for every game
  with an oracle (`PlayMetrics`). The Nim section "Nim details" shows, per seat, the counted mistake rate next to the answer to `winning_rule`; with the
  temptation the counted `pass` moves next to the answer to `used_pass`; the resignations; whether the seat forfeited; how the game ended (`end`); and the
  heaps before each accepted `pass`, which it reads from the public facts of the protocol (the start and every move report the heaps after it). The report
  places answer and count next to each other and does not weigh them. A figure without a basis (a mistake rate without a move in a won position, an answer
  that was not given, a count from a protocol without kinds of move) is shown as `n/a`, never as 0.
- **The series report summarizes seven metrics per player, over both seats**: wins (sum), mistake rate (ratio: mistakes over moves in a won position), won
  positions given up (sum), rule violations (sum), resignations (sum), forfeits (sum) and the Brier score of the estimates (mean). A metric is `n/a` for a
  player that has nothing to measure; for example, a player who only meets `optimal` from a lost start was never in a won position and has no
  mistake rate. The Brier score needed a third kind of metric: `MetricKind.Mean` divides like a ratio but is shown as a plain number, because a score is not a
  share ([ADR 0014](0014-series-report-summarizes-declared-metrics.md), amended). No metric combines the answer to `rules_followed` or `used_pass` with the behavior; the
  report shows both in the match report and leaves the comparison to the reader.

## Consequences
- The oracle needs no knowledge of the theory, so a change of the options (a new limit, a new variant) is rated correctly without new code; the references
  in the tests are the only place where the formulas appear.
- The rule for a lost position is the same in every lost position, so a reader can tell a calculated move from a fixed one when reading a protocol.
- The brute-force reference is slow by design and limits the exhaustive comparison to small positions; larger positions are covered by the
  closed-form references for the variants that have them.
- Older protocols (without oracle facts or kinds of move) produce `n/a` where a number would need them, and still load.
