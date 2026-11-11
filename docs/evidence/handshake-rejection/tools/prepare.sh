#!/usr/bin/env bash
# Handshake rejection (2026-11-11). Usage: prepare.sh <copy-name> <commit> <change>
# change: none or a defect of defect.py (throw-removed). Makes an isolated git archive
# copy in a scratch directory, records its diff and file identities, and builds it once
# in Release. Untimed. Runs from the fix/handshake-rejection-test worktree.
set -u
REPO=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
TOOLS=$REPO/docs/evidence/handshake-rejection/tools
S=${SCRATCH:-/tmp/s11-handshake-scratch}
name=$1; commit=$2; change=$3
ev=$REPO/docs/evidence/handshake-rejection/copies; mkdir -p "$ev"
copy=$S/copies/$name; rm -rf "$copy"; mkdir -p "$copy"
git -C "$REPO" archive "$commit" | tar -x -C "$copy"
pristine=$S/pristine/$name; rm -rf "$pristine"; mkdir -p "$pristine"
git -C "$REPO" archive "$commit" src | tar -x -C "$pristine"
case "$change" in
  none) ;;
  *) python3 -I "$TOOLS/defect.py" "$copy" "$change" ;;
esac
(cd "$S/pristine" && diff -ruN "$name/src" "$copy/src" | sed "s#$copy/#copy/#") > "$ev/$name.diff"
{
  echo "copy=$name commit=$(git -C "$REPO" rev-parse "$commit") change=$change date=$(date -u +%FT%TZ)"
  for f in src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs src/main/WebsocketClientLite/Service/HandshakeHandler.cs src/main/WebsocketClientLite/Parser/HandshakeParser.cs src/main/WebsocketClientLiteTest/ReconnectTests.cs src/main/WebsocketClientLiteTest/IntegrationTests.cs src/main/WebsocketClientLiteTest/HandshakeRejectionTests.cs; do
    if [ -f "$copy/$f" ]; then echo "sha256 $(sha256sum "$copy/$f" | cut -d' ' -f1) $f"; else echo "absent $f"; fi
  done
} > "$ev/$name.identity.txt"
(cd "$copy" && dotnet build src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --nologo) > "$ev/$name.build.log" 2>&1
code=$?
echo "exit=$code" > "$ev/$name.build.exit"
echo "== $name build exit=$code"; grep -E "error|Warn|Build succeeded" "$ev/$name.build.log" | tail -3
