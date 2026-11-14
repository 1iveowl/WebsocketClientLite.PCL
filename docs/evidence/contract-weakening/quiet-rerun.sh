#!/usr/bin/env bash
# Quiet rerun of the mutation suite under the weakened 16-bit contract, and the 64-bit header
# mutant applied by hand on top of it. Usage: quiet-rerun.sh <worktree> <output of run.sh>
set -u
wt="$1"; out="$2"; cd "$wt"
step() { local name="$1"; shift; { echo "\$ $*"; echo "# started $(date -u +%FT%TZ)"; "$@"; rc=$?; echo "# exit=$rc finished $(date -u +%FT%TZ)"; } > "$out/$name.txt" 2>&1; }
git checkout -q -- verification/
git apply "$out/marker126.patch"
step marker126-5-mutations-quiet ./scripts/pdd.sh mutations --output "$out/marker126-quiet-mutations-evidence"
# The 64-bit header mutant, applied by hand on top of the weakened 16-bit contract, verified with
# the command the mutation runner uses.
python3 - <<'P'
import json; p='verification/Protocol.dfy'; s=open(p).read()
m=[x for x in json.load(open('verification/mutations/payload.json'))['mutants'] if x['id']=='HEADER-001-short-sixty-four-bit'][0]
assert s.count(m['before'])==1; open(p,'w').write(s.replace(m['before'],m['after']))
P
git diff -- verification/Protocol.dfy > "$out/marker126-6-sixty-four-bit-mutant.patch"
tool=$(bash scripts/dafny-toolchain.sh)/dafny
step marker126-6-sixty-four-bit-mutant "$tool/dafny" verify verification/Masking.dfy --verify-included-files --allow-external-contracts --solver-path "$tool/z3/bin/z3-4.12.1" --verification-time-limit 60 --cores 2
git checkout -q -- verification/
git status --short > "$out/final-status-2.txt"
