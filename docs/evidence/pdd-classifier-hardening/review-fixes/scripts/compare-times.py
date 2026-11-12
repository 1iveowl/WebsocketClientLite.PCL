"""Per-mutant elapsed seconds side by side, from mutation results.json files (S10, 2026-11-11).
usage: python3 -I compare-times.py label=path ..."""
import json, sys
cols = [a.split("=", 1) for a in sys.argv[1:]]
data = [{e["id"]: e for e in json.load(open(p))["experiments"]} for _, p in cols]
ids = list(data[0])
print("| Experiment | " + " | ".join(l for l, _ in cols) + " |")
print("| --- |" + " --- |" * len(cols))
for i in ids:
    cells = []
    for d in data:
        e = d.get(i)
        if not e or e.get("elapsed_seconds") is None: cells.append(e["status"] if e else "absent")
        else: cells.append(f"{e['elapsed_seconds']:.1f} s, {e['status']}" + (", timed out" if e.get("timed_out") else ""))
    print(f"| `{i}` | " + " | ".join(cells) + " |")
for (l, _), d in zip(cols, data):
    t = [e["elapsed_seconds"] for k, e in d.items() if k != "baseline" and e.get("elapsed_seconds") is not None]
    print(f"\n{l}: mutants with a time {len(t)}, min {min(t):.1f} s, max {max(t):.1f} s, over 30 s (a third of the 90 s budget): {sum(x > 30 for x in t)}", end="")
print()
