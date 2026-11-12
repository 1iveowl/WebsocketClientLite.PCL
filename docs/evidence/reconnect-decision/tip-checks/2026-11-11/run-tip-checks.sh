#!/usr/bin/env bash
# M3 review fix, 2026-11-11: the tip checks (finding 4) and the final-tree checks of
# PR #142, on the checked-out commit from a clean tree. Every heavy check holds the
# shared lock ~/worktrees/.timing.lock (controller note of 2026-11-11). Each check's
# output goes to its own log; run.log has each command, its exit code and its times.
# Usage: run-tip-checks.sh <repository> <output outside the tracked tree>
set -uo pipefail
repo="$1"; out="$2"
cd "$repo"
mkdir -p "$out"
log="$out/run.log"
lock="$HOME/worktrees/.timing.lock"
echo "start=$(date -u +%FT%TZ) head=$(git rev-parse HEAD) status_porcelain_lines=$(git status --porcelain | wc -l)" > "$log"
step() { # name, locked (yes|no), command...
  local name="$1" locked="$2"; shift 2
  echo "== $name start $(date -u +%FT%TZ) lock=$locked: $*" >> "$log"
  if [[ "$locked" == yes ]]; then
    flock "$lock" bash -c 'echo "== '"$name"' lock acquired $(date -u +%FT%TZ)" >> "$0"; shift; "$@"' "$log" _ "$@" > "$out/$name.log" 2>&1
  else
    "$@" > "$out/$name.log" 2>&1
  fi
  echo "== $name exit=$? end $(date -u +%FT%TZ)" >> "$log"
}
# The tip changes documents and evidence only: the source equals the base commit.
step source-diff no git diff --stat fa96d59 HEAD -- src tools scripts verification/Masking.dfy verification/Protocol.dfy verification/generated verification/requirements.json verification/boundaries.json verification/mutations verification/stryker-close-dispositions.json test-all-tfms.sh GitVersion.yml
step build yes dotnet build src/main/WebsocketClientLite.sln -c Release --nologo
step matrix yes ./test-all-tfms.sh Release
step coverage-run yes dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --nologo --collect:"XPlat Code Coverage" --results-directory "$out/coverage"
step coverage-gate no ./scripts/check-coverage.sh "$out/coverage" 70
step pdd-tests yes dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release --nologo
step validate no ./scripts/pdd.sh validate
step capture-protocol yes ./scripts/pdd.sh capture --job protocol --output "$out/pdd/protocol"
step capture-tests yes ./scripts/pdd.sh capture --job tests --output "$out/pdd/tests"
step capture-controlled yes ./scripts/pdd.sh capture --job controlled --output "$out/pdd/controlled"
step capture-stryker yes ./scripts/pdd.sh capture --job stryker --output "$out/pdd/stryker"
step aggregate no ./scripts/pdd.sh aggregate --output "$out/pdd"
echo "end=$(date -u +%FT%TZ) head=$(git rev-parse HEAD) status_porcelain_lines=$(git status --porcelain | wc -l)" >> "$log"
