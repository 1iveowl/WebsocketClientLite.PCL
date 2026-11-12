#!/usr/bin/env bash
# S6 checks, 2026-11-11: the four pdd.sh capture jobs at the production commit from a
# clean worktree, each into a new directory, then aggregate. The protocol job runs
# under the shared timing lock because its per-mutant times are reported (lesson 3);
# the other jobs are untimed checks (controller note, item 2).
# Usage: run-captures.sh <repository> <output>
set -uo pipefail
repo="$1"; out="$2"
cd "$repo"
mkdir -p "$out"
log="$out/capture-run.log"
{
  echo "start=$(date -u +%FT%TZ) head=$(git rev-parse HEAD) status_porcelain_lines=$(git status --porcelain | wc -l)"
} > "$log"
step() { # name, command...
  local name="$1"; shift
  echo "== $name start $(date -u +%FT%TZ)" >> "$log"
  "$@" > "$out/$name.console.log" 2>&1
  echo "== $name exit=$? end $(date -u +%FT%TZ)" >> "$log"
}
flock "$HOME/worktrees/.timing.lock" bash -c '
  echo "== protocol lock acquired $(date -u +%FT%TZ)" >> "$1"
  { echo "== before protocol at $(date -u +%FT%TZ), load $(cut -d" " -f1-3 /proc/loadavg)"; ps -eo pid,etime,args | grep -E "claude -p|dotnet" | grep -v grep; } > "$2/processes-before-protocol.txt"
  echo "== protocol start $(date -u +%FT%TZ)" >> "$1"
  ./scripts/pdd.sh capture --job protocol --output "$2/pdd/protocol" > "$2/protocol.console.log" 2>&1
  echo "== protocol exit=$? end $(date -u +%FT%TZ)" >> "$1"
' _ "$log" "$out"
step tests ./scripts/pdd.sh capture --job tests --output "$out/pdd/tests"
step controlled ./scripts/pdd.sh capture --job controlled --output "$out/pdd/controlled"
step stryker ./scripts/pdd.sh capture --job stryker --output "$out/pdd/stryker"
step aggregate ./scripts/pdd.sh aggregate --output "$out/pdd"
echo "end=$(date -u +%FT%TZ) head=$(git rev-parse HEAD) status_porcelain_lines=$(git status --porcelain | wc -l)" >> "$log"
