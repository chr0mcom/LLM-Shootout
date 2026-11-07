# 0005 - Players, seats and wording as templates

Status: accepted (2026-11-06), amended 2026-11-07

## Decision
- **Seats** are `enum Seat { A, B }`. The arena iterates `Seat.All` and does not call `Opponent` except where
  a game is explicitly two-seat. A three-seat game is a documented later change: add `C`, adapt the prompt
  templates ("opponent" becomes a name), and the per-seat identity lists.
- **Players** implement `IPlayer.RespondAsync(PlayerCall)`. The call carries the finished prompt, a response
  schema, the attempt number and a `SampleValidAnswer` function. The arena builds the sampling function from
  the game (`SampleLegalMove`) or from the debriefing questions, so `RandomPlayer` also answers the
  debriefing and no player knows the game. A console player for humans reads the prompt and returns text.
- **Parsing is the arena's job.** Players return raw text (and reasoning). Every player, including random and
  scripted ones, goes through the same parser, validator and classifier.
- **Wording** is never built in C#. Games return `TemplateRef` (key plus values) for rules, views,
  instructions, error messages and facts. Templates live in one Markdown file per language per game
  (`## key` sections), embedded as resources; the file hash is recorded in the protocol. Tests assert on the
  values and snapshot the rendered prompts.

## Advisor input
Opus: structured views and templates instead of finished strings (multi-language, tests on records); `PlayerCall`
independent of `TurnRequest`; keep `SampleLegalMove`, add `AllowedValues`. Codex suggested structured projections, sampler
bound to the concrete turn, enum vs struct for seats (struct suggested, enum accepted by Opus). Adopted except
`Seat` as struct: the enum is shorter in print and JSON, and the break for three seats is limited to one file.

## Consequences
One concept ("text is a template plus values") is used for rules, views, facts and errors.

Amended 2026-11-07: the opponent is described the same way for every kind of player; see [0011](0011-description-of-the-opponent.md). The identity scanner remains a technical marker.

Amended 2026-11-07: the call has a last, optional member `BestAnswer` for the player type `optimal`, set only for a game that can name the best move; no other player reads it and no player knows the game ([0020](0020-optimal-player-and-oracle-capability.md)).
