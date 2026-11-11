#!/usr/bin/env bash
# S6 timing for Experiment 1 (reconnect lifecycle decision), 2026-11-11.
# S5's baseline/measure.sh with three additions, so that the same commands run on
# the after commit and on the base commit:
#   1. before each timed command, the processes named in the controller note
#      (`ps -eo pid,etime,args | grep -E 'claude -p|dotnet' | grep -v grep`) are
#      appended to processes.txt;
#   2. host.txt also records the tree hash, because the copies are git archive
#      extracts committed into a scratch repository (see run-locked.sh);
#   3. when the copy has ReconnectDecisionTests, three net10.0 --no-build runs of
#      the plain tests follow S5's commands.
# Run under the shared timing lock by run-locked.sh. Usage: measure.sh <copy> <output-directory>
set -uo pipefail
wt="$1"; out="$2"
mkdir -p "$out"
cd "$wt"
proj=src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj
filter='FullyQualifiedName~WebsocketClientLiteTest.ReconnectTests'
plain='FullyQualifiedName~WebsocketClientLiteTest.ReconnectDecisionTests'

{
  echo "date_utc=$(date -u +%FT%TZ)"
  echo "head=$(git rev-parse HEAD)"
  echo "tree=$(git rev-parse 'HEAD^{tree}')"
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
  {
    echo "== before $name at $(date -u +%FT%TZ), load $(cut -d' ' -f1-3 /proc/loadavg)"
    ps -eo pid,etime,args | grep -E 'claude -p|dotnet' | grep -v grep
  } >> "$out/processes.txt"
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

# S6 addition: the plain decision tests, where they exist. The net10.0 suite runs above
# built the test project; plain-build confirms that, so the timed runs need no build.
if [[ -f src/main/WebsocketClientLiteTest/ReconnectDecisionTests.cs ]]; then
  timed plain-build dotnet build "$proj" -c Release -p:LibTfm=net10.0 --nologo
  for i in 1 2 3; do
    timed "plain-net10.0-repeat-$i" dotnet test "$proj" -c Release -p:LibTfm=net10.0 --no-build --nologo \
      --filter "$plain" --logger "trx;LogFileName=plain-net10.0-repeat-$i.trx" --results-directory "$out/trx"
  done
fi

echo "load_after=$(cut -d' ' -f1-3 /proc/loadavg)" >> "$out/host.txt"
echo "status_porcelain_lines_after=$(git status --porcelain | wc -l)" >> "$out/host.txt"
