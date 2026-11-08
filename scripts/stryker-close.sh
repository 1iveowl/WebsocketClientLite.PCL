#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
dotnet tool restore
stryker_output="${PDD_STRYKER_OUTPUT:-$root/artifacts/stryker-close}"
mkdir -p "$stryker_output"
run_log="$(mktemp "$stryker_output/run-XXXXXX.log")"
echo "Stryker log: $run_log"
cd src/main/WebsocketClientLiteTest
dotnet tool run dotnet-stryker -- --config-file stryker-config.json \
  --output "$stryker_output" --skip-version-check "$@" 2>&1 | tee "$run_log"
