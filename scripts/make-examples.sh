#!/usr/bin/env bash
# Regenerates the example protocols in matches/examples/ with random players (no API key, no cost).
# Real model runs of the article series are stored by their authors, not here.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet build --nologo --verbosity quiet >/dev/null
SHOOTOUT=(dotnet src/LlmShootout.Cli/bin/Debug/net10.0/shootout.dll)
rm -rf matches/examples
mkdir -p matches/examples

"${SHOOTOUT[@]}" series shootouts/split-or-steal.json --a random --b random --quiet --out matches/examples
"${SHOOTOUT[@]}" play split-or-steal --a random --b random --seed 1 --lang en --quiet --out matches/examples

find matches/examples -maxdepth 2 -mindepth 1 -type d | sort
