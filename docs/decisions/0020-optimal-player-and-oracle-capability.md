# 0020 - The player type optimal, the oracle as a capability of the game, and refusal at set-up

Status: accepted (2026-11-07)

## Context
A reference player is the cleanest yardstick for strength: it never errs in a position that can be won, it costs nothing, and the same seed
gives the same match. To play the best move it has to know the game, but the players of the arena are deliberately ignorant of it
([ADR 0005](0005-players-seats-templates.md)): a `PlayerCall` carries a finished prompt, a schema and a way to produce a valid sample answer, and the factory that
creates a player does not know the game. A game that cannot name the best move cannot be played by such a player at all, and this has to be
found out when the match is set up, not after a directory has been written or a model has been paid for.

## Decision
- **The capability is the interface of [ADR 0019](0019-oracle-rates-every-move.md).** A game that implements `IGameOracle` can name the best move; no
  second interface exists. `ChooseMove(turn, assessment, rng)` is the part that serves the player.
- **The way to the player is one optional member of the call.** `PlayerCall` gets a last parameter `Func<SeededRng, string>? BestAnswer = null`.
  `TurnExecutor` sets it only if `Game is IGameOracle` and the turn does not forbid a move; it builds the whole JSON answer there: a thought from the
  arena template `optimal.thought` ("Best move.", "Bester Zug."), the move from `ChooseMove` with the assessment the arena made for the turn (the
  oracle is not asked a second time), the estimate (100 won, 0 lost, 50 drawn or unknown) if the turn does not forbid it, and the neutral sample
  message if a message is required. No other player reads it, and no player has to know the game.
- **`optimal` is a player without a model.** `Players/OptimalPlayer.cs` answers `(request.BestAnswer ?? request.SampleValidAnswer)(rng)` with its own
  `SeededRng`: a tie between best moves is decided by the seed, and a match can be repeated. It never resigns and never plays a forbidden action, because
  the best moves contain neither. For turns without a move and for the debriefing it uses the sample answer, which is deterministic for the debriefing
  ([ADR 0013](0013-standard-debriefing-and-consistent-sample-answers.md)). It costs nothing. `PlayerFactory` creates it from the specification `optimal`
  (the name is reserved for endpoints), and `SeriesRunner` creates it anew for every match from the match seed, like the random player.
- **Refusal at set-up.** `IPlayer.RequiresOracle` is false by default and true for `optimal`; a player that wraps another one passes it on
  (`ProgressPlayer` of the web interface). `MatchPreflight.Ensure(plan, game)` throws a `GameOptionsException` with the message
  `Player 'optimal' needs a game that can name the best move; '<id>' cannot.` It is called
  - in `MatchRunner.RunAsync`, after the game has been created and before the protocol writer opens the directory, so a refused match leaves nothing behind;
  - in `SeriesRunner` while it validates the game options, before the first match, so no paid match runs before the refusal;
  - in the `RunManager` of the web interface, synchronously before the run exists, so the page gets a 400 with the message.
  On the command line the exception is a usage error (exit code 2).
- **The web interface suggests `optimal`** next to `random`; the help text of `--a` names it. `scripted:` stays unavailable in the web interface.

## Consequences
- The `optimal` player needs a game with an oracle. The production games do not have one yet, so a match with `optimal` is refused for them until a game does.
- A game that wants `optimal` documents its move for a lost position; the player has no rule of its own for that case.
- The oracle is asked once per turn: the assessment is used for the rating, the report and the player.
- Checking at set-up costs one extra creation of the game in the web interface and the series (the options are validated that way already).
