#!/usr/bin/env bash
# Usage: run-p4.sh <run> <copy dir> <out dir> <commit> <edit description>
set -uo pipefail
run=$1 copy=$2 out=$3 commit=$4 edit=$5
cd "$copy"
start=$(date +%s%3N)
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release -p:LibTfm=net11.0 \
  --filter "FullyQualifiedName~ReconnectTests.EndedConnectionReportsNotConnected" \
  --results-directory "$out" --logger "trx;LogFileName=$run.trx" --logger "console;verbosity=normal" > "$out/$run.log" 2>&1
code=$?
echo "exit=$code ms=$(( $(date +%s%3N) - start ))" > "$out/$run.exit"
{
  echo "run=$run commit=$commit change=$edit date=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  for f in src/main/WebsocketClientLite/ClientWebSocketRx.cs src/main/WebsocketClientLiteTest/ReconnectTests.cs; do
    echo "sha256 $(sha256sum "$f" | cut -d' ' -f1) $f"
  done
} > "$out/$run.identity.txt"
cat "$out/$run.exit"
