#!/usr/bin/env bash
# Usage: run.sh <run> <copy dir> <out dir> <commit> <edit description> [library target]
set -uo pipefail
run=$1 copy=$2 out=$3 commit=$4 edit=$5 tfm=${6:-netstandard2.0}
cd "$copy"
start=$(date +%s%3N)
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release -p:LibTfm=$tfm \
  --filter "FullyQualifiedName~CloseBoundaryTests.ParserCompletionAwaitsFallbackCloseBeforeObserverCompletion" \
  --results-directory "$out" --logger "trx;LogFileName=$run.trx" --logger "console;verbosity=normal" > "$out/$run.log" 2>&1
code=$?
echo "exit=$code ms=$(( $(date +%s%3N) - start )) target=$tfm" > "$out/$run.exit"
{
  echo "run=$run commit=$commit change=$edit target=$tfm date=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  for f in src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs src/main/WebsocketClientLiteTest/CloseBoundaryTests.cs; do
    echo "sha256 $(sha256sum "$f" | cut -d' ' -f1) $f"
  done
} > "$out/$run.identity.txt"
cat "$out/$run.exit"
