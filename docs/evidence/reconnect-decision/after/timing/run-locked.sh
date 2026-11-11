#!/usr/bin/env bash
# S6 timing run, 2026-11-11: the after commit and, right next to it, a rerun of S5's
# timing commands on the base commit, both under one hold of the shared timing lock
# (controller note, items 2 and 6).
# Usage: run-locked.sh <repository> <after-commit> <base-commit> <scratch> <output>
set -euo pipefail
repo="$1"; after="$2"; base="$3"; scratch="$4"; out="$5"
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$out"
prepare() { # commit, directory: git archive copy, committed into a scratch repository
  local commit="$1" dir="$2"
  rm -rf "$dir"; mkdir -p "$dir"
  git -C "$repo" archive "$commit" | tar -x -C "$dir"
  git -C "$dir" init -q
  # -f: the repository tracks some files its .gitignore matches (evidence logs).
  git -C "$dir" add -A -f
  # The scratch repository has no identity of its own; use the repository user's. Never pushed.
  git -C "$dir" -c user.name="$(git -C "$repo" config user.name)" -c user.email="$(git -C "$repo" config user.email)" \
    -c commit.gpgsign=false commit -q -m "git archive of $commit"
  local want got
  want="$(git -C "$repo" rev-parse "$commit^{tree}")"; got="$(git -C "$dir" rev-parse 'HEAD^{tree}')"
  echo "$commit tree=$want copy=$dir copy_head=$(git -C "$dir" rev-parse HEAD) copy_tree=$got" >> "$out/copies.txt"
  [[ "$want" == "$got" ]] || { echo "tree mismatch for $commit" >&2; exit 1; }
}
prepare "$after" "$scratch/timing-after"
prepare "$base" "$scratch/timing-base"
echo "lock_requested=$(date -u +%FT%TZ)" > "$out/lock.txt"
flock "$HOME/worktrees/.timing.lock" bash -c '
  set -uo pipefail
  echo "lock_acquired=$(date -u +%FT%TZ)" >> "$1/lock.txt"
  bash "$2/measure.sh" "$3/timing-after" "$1/after"
  bash "$2/measure.sh" "$3/timing-base" "$1/base-rerun"
  echo "lock_released=$(date -u +%FT%TZ)" >> "$1/lock.txt"
' _ "$out" "$here" "$scratch"
