#!/usr/bin/env bash
# Handshake rejection (2026-11-11). Usage: batch.sh <batch-name> <copy-name> <runs> <plain|loaded> <tests>
# tests: covering (the four test cases Stryker lists as covering line 95) or
# covering+new (the same and HandshakeRejectionTests). Runs the prebuilt copy
# <runs> times with dotnet test --no-build. Loaded: one busy loop per core
# (nproc) for the whole batch. The caller holds the timing lock for the whole
# batch: flock ~/worktrees/.timing.lock batch.sh ...
# The process list (names only) is recorded at the start and the end.
set -u
REPO=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
S=${SCRATCH:-/tmp/s11-handshake-scratch}
batch=$1; name=$2; runs=$3; mode=$4; tests=$5
copy=$S/copies/$name
ev=$REPO/docs/evidence/handshake-rejection/runs/$batch; mkdir -p "$ev"
covering="FullyQualifiedName~WebsocketClientLiteTest.ReconnectTests.FailedConnectTerminatesAndRemovesRegistration|FullyQualifiedName~WebsocketClientLiteTest.IntegrationTests.InvalidAcceptNeverConnectsEvenWhenServerStaysOpen|FullyQualifiedName~WebsocketClientLiteTest.IntegrationTests.Non101HandshakeResponse_FailsFast_WithStatusCode"
case "$tests" in
  covering) filter=$covering ;;
  covering+new) filter="$covering|FullyQualifiedName~WebsocketClientLiteTest.HandshakeRejectionTests" ;;
  *) echo "unknown tests: $tests"; exit 2 ;;
esac
procs() { ps -eo pid,etime,comm | grep -E 'claude|dotnet' | grep -v grep; }
{
  echo "batch=$batch copy=$name runs=$runs mode=$mode tests=$tests nproc=$(nproc) date=$(date -u +%FT%TZ)"
  echo "timing lock: held by the caller (flock ~/worktrees/.timing.lock) for the whole batch"
  echo "filter=$filter"
} > "$ev/batch.txt"
{ echo "# start $(date -u +%FT%TZ)"; procs; } > "$ev/processes-start.txt"
loaders=()
if [ "$mode" = loaded ]; then
  for _ in $(seq "$(nproc)"); do (exec yes > /dev/null) & loaders+=($!); done
  echo "load: ${#loaders[@]} busy loops (yes > /dev/null) started $(date -u +%FT%TZ)" >> "$ev/batch.txt"
  sleep 2
fi
for i in $(seq -w 1 "$runs"); do
  start=$(date +%s%N)
  (cd "$copy" && dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --no-build \
    --filter "$filter" --results-directory "$ev" --logger "trx;LogFileName=run-$i.trx" \
    --logger "console;verbosity=normal") > "$ev/run-$i.log" 2>&1
  code=$?
  echo "exit=$code ms=$(( ($(date +%s%N) - start) / 1000000 ))" > "$ev/run-$i.exit"
  echo "$batch run-$i exit=$code $(grep -E '^(Passed|Failed)!' "$ev/run-$i.log" | head -1)"
done
if [ "${#loaders[@]}" -gt 0 ]; then
  kill "${loaders[@]}" 2>/dev/null; wait "${loaders[@]}" 2>/dev/null
  echo "load: stopped $(date -u +%FT%TZ)" >> "$ev/batch.txt"
fi
{ echo "# end $(date -u +%FT%TZ)"; procs; } > "$ev/processes-end.txt"
