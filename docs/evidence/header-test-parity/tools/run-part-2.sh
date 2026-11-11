#!/usr/bin/env bash
# S4 (part 2). Usage: run-part-2.sh <run-name> <commit> <change> <evidence-subdir>
# change: none, replay (a779602 with the replay file added), a defect of defect.py
# (size-limit, reads-past-header) or a defect of defect-part-2.py.
set -u
REPO=/workspaces/WebsocketClientLite.PCL
TOOLS=$REPO/docs/evidence/header-test-parity/tools
S=/tmp/claude-1000/-workspaces-WebsocketClientLite-PCL/ce292298-d831-4ecc-81a7-b7baff43302d/scratchpad
run=$1; commit=$2; change=$3; sub=$4
ev=$REPO/docs/evidence/header-test-parity/part-2/$sub; mkdir -p "$ev"
copy=$S/copies/$sub-$run; rm -rf "$copy"; mkdir -p "$copy"
git -C "$REPO" archive "$commit" | tar -x -C "$copy"
pristine=$S/pristine/$sub-$run; rm -rf "$pristine"; mkdir -p "$pristine"
git -C "$REPO" archive "$commit" src verification | tar -x -C "$pristine"
case "$change" in
  none) ;;
  replay) cp "$TOOLS/ParserRetentionTests.a779602-replay-part-2.cs" "$copy/src/main/WebsocketClientLiteTest/ParserRetentionTests.cs" ;;
  size-limit|reads-past-header) python3 -I "$TOOLS/defect.py" "$copy" "$change" ;;
  *) python3 -I "$TOOLS/defect-part-2.py" "$copy" "$change" ;;
esac
(cd "$S/pristine" && diff -ruN "$sub-$run/src" "$copy/src" | sed "s#$copy/#copy/#"; diff -ruN "$sub-$run/verification" "$copy/verification" | sed "s#$copy/#copy/#") > "$ev/$run.diff"
filter="FullyQualifiedName~.RejectsMalformedFrames|FullyQualifiedName~.TruncatedHeadersNeverProduceFrames|FullyQualifiedName~.Parse_FrameExceedingMaxFrameSize_Throws|FullyQualifiedName~.RejectsMessageAboveLimit|FullyQualifiedName~.InvalidHeadersRejectBeforePayloadRead|FullyQualifiedName~.ConfiguredLimitAcceptsLimitAndRejectsSuccessorFromHeader|FullyQualifiedName~.AccumulatedLimitRejectsBeforePayloadRead"
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
