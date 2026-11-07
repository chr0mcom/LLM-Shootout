#!/usr/bin/env bash
# Regenerates the example protocols in matches/examples/ for Nim (no API key, no cost).
# The players are the random player, the optimal player and one scripted player (docs/examples/nim-pass-script.json).
# Usage: scripts/make-examples.sh [output-directory]   (default: matches/examples; SHOOTOUT_NO_BUILD=1 skips the build)
# Real model runs of the article series are stored by their authors, not here.
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="${1:-matches/examples}"
if [[ -z "${SHOOTOUT_NO_BUILD:-}" ]]; then
  dotnet build --nologo --verbosity quiet >/dev/null
fi
SHOOTOUT=(dotnet src/LlmShootout.Cli/bin/Debug/net10.0/shootout.dll)
rm -rf "$OUT"
mkdir -p "$OUT"

# A random player against the optimal player, in English and in German.
"${SHOOTOUT[@]}" play nim --a random --b optimal --seed 1 --first-seat A --lang en --quiet --out "$OUT"
"${SHOOTOUT[@]}" play nim --a optimal --b random --seed 2 --first-seat B --lang de --quiet --out "$OUT"

# The command pass is accepted but forbidden: the random player never plays it, so this match has no rule violation.
"${SHOOTOUT[@]}" play nim --a optimal --b random --seed 3 --first-seat A --lang en --quiet \
  --game-option temptation=pass --game-option allowResign=true --out "$OUT"

# A hand-written script that passes once in a lost position: the report shows a rule violation and a calibration table.
"${SHOOTOUT[@]}" play nim --a scripted:docs/examples/nim-pass-script.json --b optimal --seed 4 --first-seat A --lang en --quiet \
  --game-option 'heaps=[1,4,5]' --game-option temptation=pass --game-option allowResign=true --out "$OUT"

# The series file for the rule compliance experiment, with a random player on seat A and a series report.
"${SHOOTOUT[@]}" series shootouts/nim-vs-optimal.json --a random --lang en --quiet --out "$OUT"

find "$OUT" -maxdepth 2 -mindepth 1 -type d | sort
