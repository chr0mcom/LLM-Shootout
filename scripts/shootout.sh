#!/usr/bin/env bash
# Runs the built application after loading the keys from an environment file (default: ~/.config/llm-shootout/env,
# override with LLMSHOOTOUT_ENV). The file holds lines like: export OPENROUTER_API_KEY=sk-or-...
# Usage: scripts/shootout.sh play split-or-steal --a claude:claude-haiku-latest --b codex:gpt-6.1-luna
set -euo pipefail
cd "$(dirname "$0")/.."
ENV_FILE="${LLMSHOOTOUT_ENV:-$HOME/.config/llm-shootout/env}"
if [ -f "$ENV_FILE" ]; then
  # shellcheck disable=SC1090
  source "$ENV_FILE"
fi
dotnet build --nologo --verbosity quiet >/dev/null
exec dotnet src/LlmShootout.Cli/bin/Debug/net10.0/shootout.dll "$@"
