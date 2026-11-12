#!/usr/bin/env bash
# Usage: bench.sh <evidence-dir> <run-number>, run from inside the tree to benchmark.
# It benchmarks the git tree that contains the current directory, and records that
# tree's commit and whether it is clean inside the lock, just before the run.
# Until 2026-11-11 this script ran `cd /home/vscode/worktrees/s7` instead (M4 review
# finding 6); the S7 and S8 runs used that version, and both steps worked in that tree.
set -u
E="$1"; N="$2"
mkdir -p "$E"; E=$(cd "$E" && pwd)
cd "$(git rev-parse --show-toplevel)" || exit 1
{
  echo "run: $N"
  echo "date (UTC) before lock: $(date -u +%FT%TZ)"
} > "$E/benchmark-run-$N.meta"
flock ~/worktrees/.timing.lock bash -c '
  E="$1"; N="$2"
  {
    echo "lock acquired (UTC): $(date -u +%FT%TZ)"
    echo "commit (git rev-parse HEAD in the benchmarked tree): $(git rev-parse HEAD)"
    echo "tree changes (git status --porcelain --untracked-files=no | wc -l): $(git status --porcelain --untracked-files=no | wc -l)"
    echo "ps -eo pid,etime,comm | grep -E \"claude|dotnet|VBCSCompiler\" (taken just before the run):"
    ps -eo pid,etime,comm | grep -E "claude|dotnet|VBCSCompiler"
    echo "uptime: $(uptime)"
  } >> "$E/benchmark-run-$N.meta"
  start=$(date +%s.%N)
  DOTNET_TieredCompilation=0 dotnet run --project verification/benchmark/MaskingBenchmark.csproj -c Release > "$E/benchmark-run-$N.out" 2>&1
  rc=$?
  end=$(date +%s.%N)
  {
    echo "command: DOTNET_TieredCompilation=0 dotnet run --project verification/benchmark/MaskingBenchmark.csproj -c Release"
    echo "exit=$rc wall_seconds=$(awk -v a="$start" -v b="$end" "BEGIN{printf \"%.2f\", b-a}")"
    echo "finished (UTC): $(date -u +%FT%TZ)"
  } >> "$E/benchmark-run-$N.meta"
' _ "$E" "$N"
