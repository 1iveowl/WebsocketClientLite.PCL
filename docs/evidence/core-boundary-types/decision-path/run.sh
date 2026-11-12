#!/usr/bin/env bash
# Evidence-only probe for S8, 2026-11-11.
# Usage: run.sh <source tree> <output prefix> <runs>
# Builds the probe against <source tree> in a temporary directory, then runs it
# <runs> times, each under the shared timing lock, recording ps and load first.
# Each .meta records the commit of <source tree>: git rev-parse HEAD when it is a git
# tree, or git get-tar-commit-id of <source tree>/../source.tar when it was extracted
# from a git archive saved there (added 2026-11-11, M4 review finding 6).
set -euo pipefail
src=$(cd "$1" && pwd); out=$2; runs=$3
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d /tmp/s8-probe-XXXXXX)
cp "$here/DecisionPathProbe.csproj.txt" "$work/DecisionPathProbe.csproj"
cp "$here/Program.cs" "$work/Program.cs"
dotnet build "$work/DecisionPathProbe.csproj" -c Release -p:Src="$src" -o "$work/bin" > "$out-build.log" 2>&1 || { tail -20 "$out-build.log"; exit 1; }
tail -3 "$out-build.log"
for n in $(seq 1 "$runs"); do
  flock ~/worktrees/.timing.lock bash -c '
    meta=$1; outfile=$2; dll=$3
    { echo "date_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"; echo "lock=flock ~/worktrees/.timing.lock";
      echo "commit=$(git -C "$4" rev-parse HEAD 2>/dev/null || git get-tar-commit-id 2>/dev/null < "$4/../source.tar" || echo unknown)";
      echo "loadavg=$(cat /proc/loadavg)"; echo "ps:"; ps -eo pid,etime,comm | grep -E "claude|dotnet|VBCSCompiler" || true; } > "$meta"
    start=$(date +%s.%N)
    DOTNET_TieredCompilation=0 dotnet "$dll" > "$outfile" 2>&1
    rc=$?
    end=$(date +%s.%N)
    echo "wall_seconds=$(python3 -c "print(round($end-$start,2))")" >> "$meta"
    echo "exit=$rc" >> "$meta"' _ "$out-run-$n.meta" "$out-run-$n.out" "$work/bin/DecisionPathProbe.dll" "$src"
  cat "$out-run-$n.out"
done
