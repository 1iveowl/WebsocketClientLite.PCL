"""Count the stateful region of ClientWebSocketRx.cs for Experiment 1, before or after.

Usage: python3 -I count_region.py <spec.json> <repository root>

Written 2026-11-11 after the M3 review of PR #142 (finding 10). It replaces the two
copies baseline/count_region.py (S5) and after/count_region_after.py (S6), which are
kept unchanged as the scripts that made the recorded counts. This script holds the
counting rules once; a spec gives only what differs between the two commits: the
file, the anchor lines, the line ranges of each part, the lifecycle-state patterns
and any new files to count. before.json and after.json reproduce both recorded counts
(see README.md).

Rules, the same for every spec:
- A line is a code line when it is not blank and does not start with "//" after
  leading whitespace. That also excludes "///" documentation lines.
- A lifecycle-state line is a code line in a part that matches a state pattern.
- A lock site is a code line in a part that contains "lock (_lifecycleGate)".
- Every anchor text must occur in its line, so a shifted file fails instead of
  being miscounted."""
import hashlib, json, os, re, sys

spec_path, root = sys.argv[1:3]
spec = json.load(open(spec_path, encoding="utf-8"))


def is_code(line):
    t = line.strip()
    return bool(t) and not t.startswith("//")


path = os.path.join(root, spec["file"])
raw = open(path, "rb").read()
lines = raw.decode("utf-8").split("\n")
for n, text in spec["anchors"].items():
    assert text in lines[int(n) - 1], f"anchor {n} moved: {lines[int(n) - 1]!r}"
patterns = {name: re.compile(rx) for name, rx in spec["state_patterns"].items()}
names = list(patterns)
print("spec", os.path.basename(spec_path))
print("file", spec["file"])
print("sha256", hashlib.sha256(raw).hexdigest())
total = {"physical": 0, "code": 0, **{f"state ({n})": 0 for n in names}, "locks": 0}
for name, a, b in spec["parts"]:
    seg = [(i, lines[i - 1]) for i in range(a, b + 1)]
    code = [(i, l) for i, l in seg if is_code(l)]
    state = {n: [(i, l) for i, l in code if p.search(l)] for n, p in patterns.items()}
    locks = [i for i, l in code if "lock (_lifecycleGate)" in l]
    counts = ", ".join(f"{len(state[n])} ({n})" for n in names)
    print(f"{name}: lines {a}-{b}, physical {len(seg)}, code {len(code)}, lifecycle-state lines {counts}, lock sites {locks}")
    shown = sorted({i: l for n in names for i, l in state[n]}.items())
    for i, l in shown:
        print(f"    {i:4}: {l.strip()}")
    total["physical"] += len(seg)
    total["code"] += len(code)
    total["locks"] += len(locks)
    for n in names:
        total[f"state ({n})"] += len(state[n])
print("total", total)
print("every lock statement in file", [i + 1 for i, l in enumerate(lines) if "lock (" in l])
for rel in spec.get("new_files", []):
    text = open(os.path.join(root, rel), "rb").read()
    ls = text.decode("utf-8").rstrip("\n").split("\n")
    print(f"{rel}: sha256 {hashlib.sha256(text).hexdigest()}, physical {len(ls)}, code {sum(is_code(l) for l in ls)}")
