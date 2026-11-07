#!/usr/bin/env bash
# Regenerates the example protocols in matches/examples/ with random players (no API key, no cost).
# Usage: scripts/make-examples.sh <game-id> [seed]
# Real model runs of the article series are stored by their authors, not here.
set -euo pipefail
cd "$(dirname "$0")/.."

GAME="${1:?Usage: scripts/make-examples.sh <game-id> [seed]}"
SEED="${2:-1}"

dotnet build --nologo --verbosity quiet >/dev/null
SHOOTOUT=(dotnet src/LlmShootout.Cli/bin/Debug/net10.0/shootout.dll)
rm -rf matches/examples
mkdir -p matches/examples

"${SHOOTOUT[@]}" play "$GAME" --a random --b random --seed "$SEED" --lang en --quiet --out matches/examples

find matches/examples -maxdepth 2 -mindepth 1 -type d | sort
