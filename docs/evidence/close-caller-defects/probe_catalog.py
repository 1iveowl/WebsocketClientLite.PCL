"""Probe every close-caller-defects catalog entry 30 times (2026-11-25).

Usage: python3 -I probe_catalog.py <repo> <commit> <scratch-dir> <out-dir> <repeat_runs.py> [<id>...]

For each entry of Evidence.CloseCallerCases (read from tools/Pdd/CloseCallerDefects.cs at
<commit>), extract a fresh `git archive <commit>` copy, apply the entry's one edit to
WebsocketSenderHandler.cs, and run repeat_runs.py with the entry's named test, 30 runs,
library net11.0. Each run's rows are kept; the summary counts, per row, how many runs
failed with the named assertion (and no TimeoutException), failed otherwise, or passed."""
import json, os, re, shutil, subprocess, sys
repo, commit, scratch, out, runner = sys.argv[1:6]
only = set(sys.argv[6:])
src = subprocess.check_output(["git", "-C", repo, "show", f"{commit}:tools/Pdd/CloseCallerDefects.cs"], text=True)
def lit(s):  # C# regular string literal body -> text
    return s.encode().decode("unicode_escape")
Q = r'"((?:[^"\\]|\\.)*)"'
pat = re.compile(r'\("([a-z-]+)",\s*' + Q + r',\s*' + Q + r',\s*"(\w+)",\s*"(\w+)",\s*' + Q + r',\s*(\d+),\s*(\d+)\)')
cases = [dict(id=m[0], before=lit(m[1]), after=lit(m[2]), cls=m[3], test=m[4], assertion=lit(m[5]), rows=int(m[6]), failing=int(m[7])) for m in pat.findall(src)]
assert len(cases) == 13, len(cases)
os.makedirs(out, exist_ok=False)
summary = []
target = "src/main/WebsocketClientLite/Service/WebsocketSenderHandler.cs"
for c in cases:
    if only and c["id"] not in only: continue
    copy = os.path.join(scratch, "probe-" + c["id"])
    shutil.rmtree(copy, ignore_errors=True); os.makedirs(copy)
    subprocess.run(f"git -C {repo} archive {commit} | tar -x -C {copy}", shell=True, check=True)
    p = os.path.join(copy, target); s = open(p).read()
    assert s.count(c["before"]) == 1, c["id"]
    open(p, "w").write(s.replace(c["before"], c["after"]))
    d = os.path.join(out, c["id"])
    flt = f"FullyQualifiedName=WebsocketClientLiteTest.{c['cls']}.{c['test']}"
    subprocess.run([sys.executable, "-I", runner, copy, d, "net11.0", "30", flt], stdout=subprocess.DEVNULL, check=False)
    r = json.load(open(os.path.join(d, "results.json")))
    rows = {}
    for run in r["runs"]:
        for x in run.get("rows") or []:
            k = rows.setdefault(x["test"].split(".")[-1], {"named": 0, "other": 0, "passed": 0})
            if x["outcome"] == "Passed": k["passed"] += 1
            elif c["assertion"] in x["message"] and "TimeoutException" not in x["message"]: k["named"] += 1
            else: k["other"] += 1
    summary.append({**{k: c[k] for k in ("id", "cls", "test", "assertion", "rows", "failing")}, "runs": len(r["runs"]), "build_exit": r["build"]["exit"], "rows_result": rows})
    print(c["id"], rows, flush=True)
    shutil.rmtree(copy, ignore_errors=True)
json.dump(summary, open(os.path.join(out, "summary.json"), "w"), indent=1)
