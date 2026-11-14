#!/usr/bin/env bash
# Recount at the head of releases/10.5.0. Usage: recount.sh <worktree> <out>
set -u
wt="$1"; out="$2"; cd "$wt"; mkdir -p "$out"
step() { local name="$1"; shift; { echo "\$ $*"; echo "# started $(date -u +%FT%TZ) at $(git rev-parse HEAD)"; "$@"; rc=$?; echo "# exit=$rc finished $(date -u +%FT%TZ)"; } > "$out/$name.txt" 2>&1; }
step 0-static python3 -I "$(dirname "$0")/static-recount.py"
step 1-check bash scripts/dafny-masking.sh --check
step 2-protocol ./scripts/pdd.sh capture --job protocol --output "$out/protocol"
step 3-tests ./scripts/pdd.sh capture --job tests --output "$out/tests"
step 4-stryker ./scripts/pdd.sh capture --job stryker --output "$out/stryker"
git status --short > "$out/final-status.txt"
echo done > "$out/DONE"
