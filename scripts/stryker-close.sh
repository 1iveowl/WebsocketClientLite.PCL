#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
dotnet tool restore
mkdir -p "$root/artifacts/stryker-close"
run_log="$(mktemp "$root/artifacts/stryker-close/run-XXXXXX.log")"
echo "Stryker log: $run_log"
cd src/main/WebsocketClientLiteTest
dotnet tool run dotnet-stryker -- --config-file stryker-config.json \
  --output "$root/artifacts/stryker-close" --skip-version-check "$@" 2>&1 | tee "$run_log"
