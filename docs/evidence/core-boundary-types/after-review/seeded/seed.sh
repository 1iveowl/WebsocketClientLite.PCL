#!/usr/bin/env bash
# S8, 2026-11-11; M4 review fixes, 2026-11-11. Seeds one defect per copy into Helper/CoreBoundaryTypes.cs of
# git archive <commit>, then runs only CoreBoundaryTypesTests (net10.0, Release).
# Usage: seed.sh <commit> <evidence dir>
set -u
commit=$1; out=$2; repo=$(git rev-parse --show-toplevel)
run() {
  id=$1; before=$2; after=$3
  copy=$(mktemp -d /tmp/m4-seed-$id-XXXXXX)
  git -C "$repo" archive "$commit" | tar -x -C "$copy"
  file="$copy/src/main/WebsocketClientLite/Helper/CoreBoundaryTypes.cs"
  python3 -I -c 'import sys; p,b,a=sys.argv[1:]; s=open(p).read(); assert s.count(b)==1, "anchor"; open(p,"w").write(s.replace(b,a))' "$file" "$before" "$after" || { echo "$id: anchor missing" > "$out/$id.out"; return; }
  { echo "id=$id"; echo "commit=$commit"; echo "before=$before"; echo "after=$after"; echo "source_sha256=$(git -C "$repo" show "$commit:src/main/WebsocketClientLite/Helper/CoreBoundaryTypes.cs" | sha256sum | cut -d' ' -f1)"; echo "mutant_sha256=$(sha256sum "$file" | cut -d' ' -f1)"; } > "$out/$id.meta"
  dotnet test "$copy/src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj" -c Release --filter "FullyQualifiedName~CoreBoundaryTypesTests" > "$out/$id.out" 2>&1
  echo "exit=$?" >> "$out/$id.meta"
  rm -rf "$copy"
}
# S8's control and four defects, unchanged anchors.
run none "internal static ReceiveState Idle => default;" "internal static ReceiveState Idle => default;"
run fromcode-accepts-3 "code is 0 or 1 or 2 or 8" "code is 0 or 1 or 2 or 3 or 8"
run received-is-event-2 "internal static CloseEvent Received => new(1);" "internal static CloseEvent Received => new(2);"
run sendstep-keeps-state "        next = new TransmitState(code);" "        next = state;"
run step-drops-state "Protocol.Step(state._code, op, fin" "Protocol.Step(0, op, fin"
# M4 review: one defect per new guarantee (findings 8, 2 and 7).
run extra-factory "    internal static CloseState Open => default;" $'    internal static CloseState Open => default;\n\n    internal static CloseState From(byte code) => new(code);'
run implicit-conversion "    internal bool IsIdle => _code == 0;" $'    public static implicit operator byte(ReceiveState state) => state._code;\n\n    internal bool IsIdle => _code == 0;'
run default-event-is-send '        : throw new InvalidOperationException("default(CloseEvent) is not a close event.");' '        : (byte)0;'
