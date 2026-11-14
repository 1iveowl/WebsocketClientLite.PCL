#!/usr/bin/env bash
# Contract-weakening experiment on HeaderValid (verification/contract-weakening.md).
# Usage: run.sh <throwaway worktree> <empty output directory>
# Make the worktree with `git worktree add --detach <path> <commit>`. The script edits its
# verification/ folder and restores it with `git checkout`; never point it at a checkout you work in.
set -u
wt="$1"; out="$2"; cd "$wt"
step() { local name="$1"; shift; { echo "\$ $*"; echo "# started $(date -u +%FT%TZ)"; "$@"; rc=$?; echo "# exit=$rc finished $(date -u +%FT%TZ)"; } > "$out/$name.txt" 2>&1; }
run_case() {
  local id="$1" old="$2" new="$3"
  git checkout -q -- verification/
  python3 - "$old" "$new" <<'P'
import sys; p='verification/Protocol.dfy'; s=open(p).read(); o,n=sys.argv[1],sys.argv[2]
assert s.count(o)==1, 'clause not found once'; open(p,'w').write(s.replace(o,n))
P
  git diff -- verification/Protocol.dfy > "$out/$id.patch"
  step "$id-1-check" bash scripts/dafny-masking.sh --check
  step "$id-2-write" bash scripts/dafny-masking.sh --write
  git diff -- verification/generated/Masking.g.cs > "$out/$id-2-generated.diff.txt"
  step "$id-3-tests" ./test-all-tfms.sh Release
  step "$id-4-mutations" ./scripts/pdd.sh mutations --output "$out/$id-mutations-evidence"
  git checkout -q -- verification/
}
echo "commit $(git rev-parse HEAD)" > "$out/identity.txt"; dotnet --version >> "$out/identity.txt"; uname -srm >> "$out/identity.txt"
run_case marker126 'else if marker == 126 then 126 <= length <= 65535 else length >= 65536' 'else if marker == 126 then 0 <= length <= 65535 else length >= 65536'
run_case marker127 'else if marker == 126 then 126 <= length <= 65535 else length >= 65536' 'else if marker == 126 then 126 <= length <= 65535 else length >= 0'
git checkout -q -- verification/
step baseline-mutations ./scripts/pdd.sh mutations --output "$out/baseline-mutations-evidence"
git status --short > "$out/final-status.txt"
echo done > "$out/DONE"
