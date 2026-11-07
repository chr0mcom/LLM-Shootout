# 0016 - JSON game options on the command line and in the web interface

Status: accepted (2026-11-07)

## Context
`--game-option key=value` understood integers, booleans and text. Games get richer options (a list of numbers, a nested object), which a series
file can express because the file is JSON, but the command line could not. The web interface had no field for game options at all, so a game
with options could only be played with its defaults from the page.

## Decision
- `RunOptionSet.ParseGameOptions` reads every value with `JsonNode.Parse` first. If that throws a `JsonException`, the value is text.
  `pot=1000` is a number, `misere=true` a boolean, `heaps=[1,4,5]` an array, `limits={"count":3,"max":9}` an object, `temptation=pass`
  text, and `--game-option 'x="1"'` forces the text 1 (the single quotes keep the shell from removing the double quotes). The pair is split at the first `=`; key and value are trimmed.
- The same key twice is a `UsageException`; before, the last value silently won.
- Compatibility: values that were numbers or booleans before and read the same as JSON do not change. Differences, all in values that no
  game used so far: `1.5` is now a number (it was text), `null` is the JSON null (it was text), and `007`, `+5` and `True` are text (the
  old parser made them 7, 5 and true). Games check the type of every option and report a wrong type with a message.
- `StartRequest.Options` is an optional `JsonObject`. `RunManager.Validate` accepts at most 2048 bytes of serialized JSON and four levels of
  nesting, treats an empty object like no options, creates the game once with `definition.Create(setup)` and turns a `GameOptionsException`
  into the `StartRejectedException` that the API answers with a 400 and the message of the game. The options go into `GameSetup.Options`, so
  the protocol records them as `gameOptions`, like for the command line and series files.
- The web form has a text field *Game options (JSON object)*. The page parses it before sending and shows a message for invalid JSON or
  a value that is not an object; an empty field sends no options.
- Series files are unchanged.

## Consequences
- Every option that a series file can express can be given on the command line and in the web interface.
- The options are untrusted input of the browser: bounded in size and depth, validated by the game before a run is reserved or paid.
- A game must read option values with checked conversions; the existing games already do.
