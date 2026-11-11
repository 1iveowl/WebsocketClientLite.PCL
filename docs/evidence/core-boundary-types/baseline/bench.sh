#!/usr/bin/env bash
# Usage: s7-bench.sh <evidence-dir> <run-number>
set -u
E="$1"; N="$2"
cd /home/vscode/worktrees/s7
{
  echo "run: $N"
  echo "date (UTC) before lock: $(date -u +%FT%TZ)"
} > "$E/benchmark-run-$N.meta"
flock ~/worktrees/.timing.lock bash -c '
  E="$1"; N="$2"
  {
    echo "lock acquired (UTC): $(date -u +%FT%TZ)"
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
