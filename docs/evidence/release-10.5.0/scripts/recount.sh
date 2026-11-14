#!/usr/bin/env bash
# Recount at the head of releases/10.5.0. Usage: recount.sh <worktree> <out>; relative paths work.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
out="$(mkdir -p "$2" && cd "$2" && pwd)"
wt="$1"; cd "$wt"
step() { local name="$1"; shift; { echo "\$ $*"; echo "# started $(date -u +%FT%TZ) at $(git rev-parse HEAD)"; "$@"; rc=$?; echo "# exit=$rc finished $(date -u +%FT%TZ)"; } > "$out/$name.txt" 2>&1; }
step 0-static python3 -I "$here/static-recount.py"
step 1-check bash scripts/dafny-masking.sh --check
step 2-protocol ./scripts/pdd.sh capture --job protocol --output "$out/protocol"
step 3-tests ./scripts/pdd.sh capture --job tests --output "$out/tests"
step 4-stryker ./scripts/pdd.sh capture --job stryker --output "$out/stryker"
git status --short > "$out/final-status.txt"
# Steps 5 and 6 were run by hand in the 10.5.0 recount (README.md); they are part of the driver since.
step 5-controlled ./scripts/pdd.sh capture --job controlled --output "$out/controlled" --command-budget 480
step 6-aggregate ./scripts/pdd.sh aggregate --output "$out"
git status --short > "$out/final-status-2.txt"
echo done > "$out/DONE"
