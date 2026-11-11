#!/usr/bin/env bash
# Usage: run.sh <run-name> <commit> <defect|none|replay> <evidence-subdir>
set -u
REPO=/workspaces/WebsocketClientLite.PCL
S=/tmp/claude-1000/-workspaces-WebsocketClientLite-PCL/6212eedc-1c62-4d8d-b543-832060c07737/scratchpad
run=$1; commit=$2; change=$3; sub=$4
ev=$REPO/docs/evidence/header-test-parity/$sub; mkdir -p "$ev"
copy=$S/copies/$sub-$run; rm -rf "$copy"; mkdir -p "$copy"
git -C "$REPO" archive "$commit" | tar -x -C "$copy"
pristine=$S/pristine/$sub-$run; rm -rf "$pristine"; mkdir -p "$pristine"
git -C "$REPO" archive "$commit" src verification | tar -x -C "$pristine"
case "$change" in
  none) ;;
  replay) cp "$S/replay/ParserRetentionTests.cs" "$copy/src/main/WebsocketClientLiteTest/ParserRetentionTests.cs" ;;
  *) python3 -I "$S/scripts/defect.py" "$copy" "$change" ;;
esac
(cd "$S/pristine" && diff -ruN "$sub-$run/src" "$copy/src" | sed "s#$copy/#copy/#"; diff -ruN "$sub-$run/verification" "$copy/verification" | sed "s#$copy/#copy/#") > "$ev/$run.diff"
filter="FullyQualifiedName~PayloadParsingTests.Parse_FrameExceedingMaxFrameSize_Throws|FullyQualifiedName~PayloadParsingTests.Parse_OversizedControlFrame_Throws|FullyQualifiedName~PayloadParsingTests.RejectsMalformedFrames|FullyQualifiedName~ParserRetentionTests.InvalidHeadersRejectBeforePayloadRead"
{
  echo "run=$run commit=$(git -C "$REPO" rev-parse "$commit") change=$change date=$(date -u +%FT%TZ)"
  for f in src/main/WebsocketClientLite/Helper/DataframeParsing.cs src/main/WebsocketClientLite/Service/TcpConnectionService.cs verification/generated/Masking.g.cs src/main/WebsocketClientLiteTest/PayloadParsingTests.cs src/main/WebsocketClientLiteTest/ParserRetentionTests.cs; do
    if [ -f "$copy/$f" ]; then echo "sha256 $(sha256sum "$copy/$f" | cut -d' ' -f1) $f"; else echo "absent $f"; fi
  done
} > "$ev/$run.identity.txt"
start=$(date +%s%N)
(cd "$copy" && dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --filter "$filter" \
  --results-directory "$ev" --logger "trx;LogFileName=$run.trx" --logger "console;verbosity=normal") > "$ev/$run.log" 2>&1
code=$?
echo "exit=$code ms=$(( ($(date +%s%N) - start) / 1000000 ))" > "$ev/$run.exit"
echo "== $sub/$run exit=$code"; grep -E "^(Passed|Failed)!|Total tests|error CS" "$ev/$run.log" | head -5
