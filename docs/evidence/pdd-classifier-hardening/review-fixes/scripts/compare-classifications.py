"""Compares status and matched clauses of every experiment in two mutation results.json files
(M6 fix of #145, 2026-11-12). usage: python3 -I compare-classifications.py <base> <after>"""
import json, sys
a, b = ({e["id"]: e for e in json.load(open(p))["experiments"]} for p in sys.argv[1:3])
same = True
for i in a:
    x, y = a[i], b.get(i, {})
    ok = (x.get("status"), x.get("matched_clauses")) == (y.get("status"), y.get("matched_clauses"))
    same &= ok
    print(f"{'same' if ok else 'DIFFERENT'}  {i}: {x.get('status')} -> {y.get('status')}, matched {len(x.get('matched_clauses') or [])} -> {len(y.get('matched_clauses') or [])}")
print("ids only in after:", sorted(set(b) - set(a)))
print("ALL SAME" if same and set(a) == set(b) else "CLASSIFICATIONS DIFFER")
