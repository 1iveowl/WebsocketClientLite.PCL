"""Repeat one test filter N times in one copy of the sources (2026-11-25).

Usage: python3 -I repeat_runs.py <copy> <out> <tfm> <runs> <filter> [<assertion>]

<copy> is a git archive copy (the caller extracts it, applies any edit and records both).
The script builds the test project once (Release, -p:LibTfm=<tfm>), then runs
`dotnet test --no-build --filter <filter>` <runs> times, each with its own TRX, and
writes results.json. Nothing is retried.

Per run: "failed" (completed, every selected row failed, and when <assertion> is given
every failure message contains it), "passed" (every row passed), otherwise
"inconclusive" (timeout, no TRX, no row ran, a mix, or a failure on another message).
The verdict is "failed on every run", "passed on every run", or "mixed" with the list.
"""
import json, os, signal, subprocess, sys, time
import xml.etree.ElementTree as ET

copy, out, tfm, runs, flt = sys.argv[1:6]
assertion = sys.argv[6] if len(sys.argv) > 6 else None
copy, out, runs = os.path.abspath(copy), os.path.abspath(out), int(runs)
os.makedirs(out, exist_ok=False)
project = "src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj"
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
env = dict(os.environ, MSBUILDDISABLENODEREUSE="1")


def run(cmd, log, timeout):
    start = time.time()
    with open(log, "w") as f:
        f.write("$ " + " ".join(cmd) + "\n")
        f.flush()
        proc = subprocess.Popen(cmd, cwd=copy, stdout=f, stderr=subprocess.STDOUT, env=env, start_new_session=True)
        try:
            code, timed_out = proc.wait(timeout=timeout), False
        except subprocess.TimeoutExpired:
            try:
                os.killpg(proc.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            proc.wait()
            code, timed_out = None, True
    return {"command": cmd, "exit": code, "timed_out": timed_out, "seconds": round(time.time() - start, 2)}


def trx(path):
    if not os.path.exists(path):
        return None
    rows = []
    for r in ET.parse(path).getroot().findall(".//t:UnitTestResult", ns):
        msg = r.find(".//t:Message", ns)
        first = (msg.text or "").strip().splitlines()[0] if msg is not None and msg.text else ""
        rows.append({"test": r.get("testName"), "outcome": r.get("outcome"), "duration": r.get("duration"), "message": first[:300]})
    return rows


report = {"made": "2026-11-25", "copy": "<scratch>/" + os.path.basename(copy), "library_target": tfm,
          "configuration": "Release", "filter": flt, "assertion": assertion, "runs": []}
report["build"] = run(["dotnet", "build", project, "-c", "Release", "-p:LibTfm=" + tfm, "--nologo"], os.path.join(out, "build.log"), 600)
if report["build"]["exit"] != 0:
    report["verdict"] = "inconclusive: build failed"
else:
    for i in range(1, runs + 1):
        name = f"run-{i:02d}"
        r = run(["dotnet", "test", project, "-c", "Release", "-p:LibTfm=" + tfm, "--no-build", "--nologo", "--filter", flt,
                 "--logger", f"trx;LogFileName={name}.trx", "--results-directory", out], os.path.join(out, name + ".log"), 300)
        rows = trx(os.path.join(out, name + ".trx"))
        r["rows"] = rows
        if r["timed_out"] or not rows:
            r["outcome"] = "inconclusive"
        elif all(x["outcome"] == "Passed" for x in rows) and r["exit"] == 0:
            r["outcome"] = "passed"
        elif all(x["outcome"] == "Failed" for x in rows) and r["exit"] == 1 and (assertion is None or all(assertion in x["message"] for x in rows)):
            r["outcome"] = "failed"
        else:
            r["outcome"] = "inconclusive"
        report["runs"].append(r)
        print(name, r["outcome"], r["seconds"], flush=True)
    outcomes = [r["outcome"] for r in report["runs"]]
    report["verdict"] = ("failed on every run" if set(outcomes) == {"failed"} else
                         "passed on every run" if set(outcomes) == {"passed"} else "mixed: " + ",".join(outcomes))
report["rows_per_run"] = sorted({len(r.get("rows") or []) for r in report["runs"]})
json.dump(report, open(os.path.join(out, "results.json"), "w"), indent=1)
print(report["verdict"])
