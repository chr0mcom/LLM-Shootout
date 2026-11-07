#!/usr/bin/env bash
# Runs every command of the application without an API key and checks that it works. Exit code 0 means success.
set -euo pipefail
cd "$(dirname "$0")/.."
unset OPENROUTER_API_KEY   # never spend money in a smoke test

dotnet build --nologo --verbosity quiet >/dev/null
SHOOTOUT=(dotnet src/LlmShootout.Cli/bin/Debug/net10.0/shootout.dll)
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

step() { printf '\n== %s\n' "$*"; }

step "games"
"${SHOOTOUT[@]}" games

step "models (saved sample response, no network)"
"${SHOOTOUT[@]}" models --from docs/examples/models-sample.json --schema-only

step "play: one match with random players"
"${SHOOTOUT[@]}" play split-or-steal --a random --b random --seed 7 --out "$WORK"

step "play: Nim with random players"
"${SHOOTOUT[@]}" play nim --a random --b random --seed 7 --out "$WORK"

step "play: Nim, the optimal player against a random player and against itself"
"${SHOOTOUT[@]}" play nim --a random --b optimal --seed 7 --out "$WORK"
"${SHOOTOUT[@]}" play nim --a optimal --b optimal --seed 7 --out "$WORK"
MATCH="$("${SHOOTOUT[@]}" play nim --a optimal --b random --seed 8 --lang en --quiet --game-option 'temptation="pass"' --game-option allowResign=true --out "$WORK")"
grep -q "winning_rule" "$MATCH/report.md" && grep -q "Nim details" "$MATCH/report.md"

step "series: Nim against the optimal player, series report with the metrics of the game"
cat > "$WORK/nim-series.json" <<'JSON'
{ "game": "nim", "seed": 5, "matches": [
    { "firstSeat": "A", "options": { "heaps": [1, 4, 5], "temptation": "pass", "allowResign": true } },
    { "firstSeat": "B", "options": { "heaps": [1, 4, 5], "temptation": "pass", "allowResign": true } } ] }
JSON
NIMSERIES="$("${SHOOTOUT[@]}" series "$WORK/nim-series.json" --a random --b optimal --lang en --quiet --out "$WORK")"
grep -q "Brier score of the estimates" "$NIMSERIES/series-report.md"

step "series: the three Nim series files with the random and the optimal player"
NIM_SERIES="$("${SHOOTOUT[@]}" series shootouts/nim.json --a random --b optimal --lang en --quiet --out "$WORK")"
"${SHOOTOUT[@]}" series shootouts/nim-vs-optimal.json --a random --lang en --quiet --out "$WORK" >/dev/null
"${SHOOTOUT[@]}" series shootouts/nim-misere.json --a optimal --b random --lang en --quiet --out "$WORK" >/dev/null
test -f "$NIM_SERIES/series-report.md" && test -d "$NIM_SERIES/01-A-first" && test -d "$NIM_SERIES/02-B-first"

step "replay, debrief and report of a Nim series"
"${SHOOTOUT[@]}" replay "$NIM_SERIES/01-A-first" --show-thoughts > "$WORK/nim-replay.txt"
head -n 12 "$WORK/nim-replay.txt"
"${SHOOTOUT[@]}" debrief "$NIM_SERIES/01-A-first"
"${SHOOTOUT[@]}" report "$NIM_SERIES"
grep -q "Metrics of random" "$NIM_SERIES/series-report.md"

step "play: Nim with the heaps, the temptation and the resignation as game options"
MATCH="$("${SHOOTOUT[@]}" play nim --a random --b optimal --seed 11 --lang en --quiet --game-option 'heaps=[1,4,5]' --game-option temptation=pass --game-option allowResign=true --out "$WORK")"
grep -q '"gameOptions":{"heaps":\[1,4,5\],"temptation":"pass","allowResign":true}' "$MATCH/events.jsonl"

step "play: quiet mode prints only the directory"
MATCH="$("${SHOOTOUT[@]}" play split-or-steal --a random --b random --communication off --seed 8 --lang en --quiet --out "$WORK")"
test -f "$MATCH/events.jsonl" && test -f "$MATCH/match.json" && test -f "$MATCH/report.md"

step "play: game options as JSON values"
MATCH="$("${SHOOTOUT[@]}" play split-or-steal --a random --b random --seed 9 --lang en --quiet --game-option pot=200 --game-option messagesPerPlayer=1 --out "$WORK")"
grep -q '"gameOptions":{"pot":200,"messagesPerPlayer":1}' "$MATCH/events.jsonl"

step "series: a temporary series file"
cat > "$WORK/series.json" <<'JSON'
{ "game": "split-or-steal", "seed": 2027, "matches": [
    { "firstSeat": "A" },
    { "firstSeat": "B", "communication": false } ] }
JSON
SERIES="$("${SHOOTOUT[@]}" series "$WORK/series.json" --a random --b random --quiet --out "$WORK")"
test -f "$SERIES/series.json" && test -f "$SERIES/series-report.md"

step "replay, debrief and report of a Split or Steal series"
"${SHOOTOUT[@]}" replay "$SERIES/01-A-first" --show-thoughts > "$WORK/replay.txt"
head -n 12 "$WORK/replay.txt"
"${SHOOTOUT[@]}" debrief "$SERIES/01-A-first"
"${SHOOTOUT[@]}" report "$SERIES"

step "play: the optimal player is refused for a game that cannot name the best move"
if "${SHOOTOUT[@]}" play split-or-steal --a optimal --b random --out "$WORK/refused" 2>"$WORK/refused.txt"; then
  echo "the optimal player was accepted for a game without an oracle" >&2
  exit 1
fi
grep -q "needs a game that can name the best move" "$WORK/refused.txt"
test ! -e "$WORK/refused"

step "usage errors give exit code 2"
set +e
"${SHOOTOUT[@]}" models --endpoint nowhere 2>/dev/null
test $? -eq 2
"${SHOOTOUT[@]}" play chess --a random --b random 2>/dev/null
test $? -eq 2
"${SHOOTOUT[@]}" play split-or-steal --a openai/gpt-4o --b random 2>/dev/null
test $? -eq 2
"${SHOOTOUT[@]}" play split-or-steal --a random --b random --game-option pot=10 --game-option pot=20 2>/dev/null
test $? -eq 2
set -e

printf '\nSmoke test passed.\n'
