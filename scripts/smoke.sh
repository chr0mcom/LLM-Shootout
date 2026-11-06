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

step "play: quiet mode prints only the directory"
MATCH="$("${SHOOTOUT[@]}" play split-or-steal --a random --b random --communication off --seed 8 --lang en --quiet --out "$WORK")"
test -f "$MATCH/events.jsonl" && test -f "$MATCH/match.json" && test -f "$MATCH/report.md"

step "series: the standard series"
SERIES="$("${SHOOTOUT[@]}" series shootouts/split-or-steal.json --a random --b random --quiet --out "$WORK")"
test -f "$SERIES/series.json" && test -f "$SERIES/series-report.md"

step "replay"
"${SHOOTOUT[@]}" replay "$SERIES/01-talk-A-first" --show-thoughts > "$WORK/replay.txt"
head -n 12 "$WORK/replay.txt"

step "annotate with a random judge"
"${SHOOTOUT[@]}" annotate "$SERIES" --judge random

step "debrief again"
"${SHOOTOUT[@]}" debrief "$SERIES/01-talk-A-first"

step "report"
"${SHOOTOUT[@]}" report "$SERIES"

step "usage errors give exit code 2"
set +e
"${SHOOTOUT[@]}" models --endpoint nowhere 2>/dev/null
test $? -eq 2
"${SHOOTOUT[@]}" play chess --a random --b random 2>/dev/null
test $? -eq 2
"${SHOOTOUT[@]}" play split-or-steal --a openai/gpt-4o --b random 2>/dev/null
test $? -eq 2
set -e

printf '\nSmoke test passed.\n'
