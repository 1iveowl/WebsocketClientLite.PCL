#!/usr/bin/env bash
# Review fixes (2026-11-11). Usage: run-review.sh <run-name> <commit> <change>
# change: none, a defect of defect.py (reads-past-header), of defect-part-2.py or of
# defect-review.py. Runs from the fix-140 worktree; copies go to a scratch directory.
set -u
REPO=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
TOOLS=$REPO/docs/evidence/header-test-parity/tools
S=${SCRATCH:-/tmp/fix140-review-scratch}
run=$1; commit=$2; change=$3
ev=$REPO/docs/evidence/header-test-parity/review-2026-11-11/failing-first; mkdir -p "$ev"
copy=$S/copies/$run; rm -rf "$copy"; mkdir -p "$copy"
git -C "$REPO" archive "$commit" | tar -x -C "$copy"
pristine=$S/pristine/$run; rm -rf "$pristine"; mkdir -p "$pristine"
git -C "$REPO" archive "$commit" src verification | tar -x -C "$pristine"
case "$change" in
  none) ;;
  size-limit|reads-past-header) python3 -I "$TOOLS/defect.py" "$copy" "$change" ;;
  known-removed|known-sequence-removed|eof-as-other) python3 -I "$TOOLS/defect-review.py" "$copy" "$change" ;;
  *) python3 -I "$TOOLS/defect-part-2.py" "$copy" "$change" ;;
esac
(cd "$S/pristine" && diff -ruN "$run/src" "$copy/src" | sed "s#$copy/#copy/#"; diff -ruN "$run/verification" "$copy/verification" | sed "s#$copy/#copy/#") > "$ev/$run.diff"
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
echo "== $run exit=$code"; grep -E "^(Passed|Failed)!|Total tests|error CS" "$ev/$run.log" | head -5
