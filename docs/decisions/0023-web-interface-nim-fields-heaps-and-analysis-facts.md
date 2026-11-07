# 0023 - Web interface: Nim fields, the heap display and the analysis facts for the viewer

Status: accepted (2026-11-07)

## Context
The web interface ([ADR 0010](0010-web-interface.md)) started with Split or Steal as its only game and one free JSON field for game options
([ADR 0016](0016-json-game-options-in-cli-and-web.md)). Nim ([ADR 0021](0021-nim-options-move-format-and-endings.md)) is the main game of the series now.
Its most important options are a list of numbers and three switches, which are tedious to type as JSON. The position of a Nim match is a set of heaps,
so a transcript of moves alone is hard to follow. The protocol also holds analysis facts (the rating of each move by the oracle, rule violations,
[ADR 0018](0018-rule-violation-an-accepted-but-forbidden-action.md), [ADR 0019](0019-oracle-rates-every-move.md)) that the players never see, and the page has to decide what to do with them.

## Decision
- **Nim is the default game of the form.** `GameCatalog` lists it first, `/api/config` keeps that order, and the first entry of the selection is the one that
  is shown. The player fields are filled with `random` for seat A and `optimal` for seat B when Nim is selected. A field that is empty, or still holds the default of the
  previous game, is replaced when the game changes; a value typed by the person is kept.
- **Simple fields for the main Nim options**, shown only for the game `nim`: the heaps as a list of whole numbers (`3,4,5`), misère, the temptation (`none` or `pass`)
  and whether resigning is a legal move. They end in the existing `StartRequest.Options`; the server accepts nothing new. The JSON field stays for everything else
  (`maxTake`, `randomHeaps`, `estimate`, `maxMoves`).
- **Fields and JSON become one object.** The JSON object is read first, the fields are put over it, so a key that is in both takes the value of the field. A field that is left at
  its default (empty heaps, misère off, temptation none, resign off) adds nothing, so a protocol without options stays without options, and the JSON can still set that key.
  The browser only checks that the heaps are whole numbers; the sizes, the number of heaps and every other rule are checked by the game on the server, and its message is shown (HTTP 400).
- **The heaps are drawn from the facts, not from the moves.** The public facts of Nim carry the position as data: `heaps` in the start fact and `heaps_after` in every
  `take` and `pass` fact. The page shows one row per heap, with a ● for each stone and a ○ for each stone taken in the last move, and updates it with each fact, live and when an
  earlier match is replayed. The numbers are read as whole numbers from the data and drawn with `textContent`; no text of a model reaches the markup.
- **The viewer sees the analysis facts live, marked as such.** The stream carries every protocol line, so the viewer receives the facts of the audience `Analysis`
  (`oracle`, `rule_violation`) while the match runs. The page shows them dimmed and dashed with the prefix "analysis:", and a rule violation in the color of a warning. The players never get
  them: the timeline filters the audience `Analysis` out of every prompt and every debriefing ([ADR 0019](0019-oracle-rates-every-move.md)); the page only reads the same protocol.
- **The Excel overview** says "Messages between players" instead of the former wording, and gains the rows resignations and exhausted turns per seat. The rule violations, the
  result with a rule violation, the figures for won positions and the calibration were added earlier; a figure without a basis is the text `n/a`.

## Consequences
- The page now has two JavaScript files (`app.js` and a module `nim.js` with the pure functions for the fields and the heaps), still without a build step and without external requests.
- A game other than Nim is unaffected: no fields, no heap display, no default players, and the JSON field is the only source of options.
- A new game with options of its own gets its fields the same way: a function that reads them, a function that merges them, and one fieldset that is shown for that game.
- The analysis facts are visible to whoever watches the page. That is intended: the page is the tool of the person who runs the match, not a view for the players.
