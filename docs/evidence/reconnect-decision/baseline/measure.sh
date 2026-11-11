#!/usr/bin/env bash
# S5 baseline for Experiment 1 (reconnect lifecycle decision), 2026-11-11.
# Runs in a detached worktree at the base commit so the source tree is clean.
# Usage: measure.sh <worktree> <output-directory>
set -uo pipefail
wt="$1"; out="$2"
mkdir -p "$out"
cd "$wt"
proj=src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj
filter='FullyQualifiedName~WebsocketClientLiteTest.ReconnectTests'

{
  echo "date_utc=$(date -u +%FT%TZ)"
  echo "head=$(git rev-parse HEAD)"
  echo "status_porcelain_lines=$(git status --porcelain | wc -l)"
  echo "uname=$(uname -srm)"
  echo "cpu=$(lscpu | sed -n 's/^Model name: *//p')"
  echo "nproc=$(nproc)"
  echo "sdk=$(dotnet --version)"
  echo "load_before=$(cut -d' ' -f1-3 /proc/loadavg)"
} > "$out/host.txt"

timed() { # name, command...
  local name="$1"; shift
  local start end code
  echo "\$ $*" > "$out/$name.log"
  start=$(date +%s.%N)
  "$@" >> "$out/$name.log" 2>&1; code=$?
  end=$(date +%s.%N)
  printf '%s exit=%d seconds=%.2f\n' "$name" "$code" "$(awk -v a="$start" -v b="$end" 'BEGIN{print b-a}')" | tee -a "$out/timings.txt"
}

timed build dotnet build src/main/WebsocketClientLite.sln -c Release --nologo

# One run per library target, each building the test project against that target.
for tfm in netstandard2.0 netstandard2.1 net8.0 net9.0 net10.0; do
  timed "suite-$tfm" dotnet test "$proj" -c Release -p:LibTfm="$tfm" --nologo \
    --filter "$filter" --logger "trx;LogFileName=suite-$tfm.trx" --results-directory "$out/trx"
done

# Three more net10.0 runs without rebuilding, for run-to-run spread.
for i in 1 2 3; do
  timed "suite-net10.0-repeat-$i" dotnet test "$proj" -c Release -p:LibTfm=net10.0 --no-build --nologo \
    --filter "$filter" --logger "trx;LogFileName=suite-net10.0-repeat-$i.trx" --results-directory "$out/trx"
done

# The seeded reconnect defect (tools/Pdd/ReconnectDefects.cs), into a fresh directory.
timed reconnect-defect ./scripts/pdd.sh reconnect-defect --output "$out/reconnect-defect"

echo "load_after=$(cut -d' ' -f1-3 /proc/loadavg)" >> "$out/host.txt"
echo "status_porcelain_lines_after=$(git status --porcelain | wc -l)" >> "$out/host.txt"
