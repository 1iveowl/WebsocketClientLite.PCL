#!/usr/bin/env bash
# For each case: apply the patch, regenerate the C#, then run --check again.
# Usage: check-after-write.sh <throwaway worktree at d3308a0> <evidence folder>
set -u
wt="$1"; ev="$2"; cd "$wt"
step() { local name="$1"; shift; { echo "\$ $*"; echo "# started $(date -u +%FT%TZ)"; "$@"; rc=$?; echo "# exit=$rc finished $(date -u +%FT%TZ)"; } > "$ev/$name.txt" 2>&1; }
for id in marker126 marker127; do
  git checkout -q -- verification/
  git apply "$ev/$id.patch"
  bash scripts/dafny-masking.sh --write > /dev/null 2>&1
  step "$id-7-check-after-write" bash scripts/dafny-masking.sh --check
done
git checkout -q -- verification/
git status --short > "$ev/final-status-3.txt"
