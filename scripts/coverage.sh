#!/usr/bin/env bash
# Line coverage of LlmShootout.Arena (generated code excluded). Fails below 85 %.
set -euo pipefail
cd "$(dirname "$0")/.."
rm -rf coverage
dotnet test \
  --coverlet \
  --coverlet-include "[LlmShootout.Arena]*" \
  --coverlet-exclude-by-attribute "GeneratedCode,CompilerGenerated,ExcludeFromCodeCoverage" \
  --coverlet-output-format cobertura \
  --coverlet-threshold 85 \
  --coverlet-threshold-type line \
  --coverlet-threshold-stat total \
  --results-directory coverage
