"""Seeded caller-side Close defects (2026-11-25), adapted from
docs/evidence/reconnect-decision/after/decision-defects/decision_defects.py.

Usage: python3 -I close_caller_defects.py <catalog.json> <side> <copy> <out> [<id>...]

<copy> is a git archive copy of the commit the side names. For each defect of the
catalog (or only the ids given) the script applies every edit exactly once, builds the
test project (Release, library net11.0), runs the controlled Close and boundary tests
(ControlledCloseTests and CloseBoundaryTests) three times and the whole test project
once, and restores the file. The unchanged copy is run the same way first and must pass.

Outcomes per run: "failed" (the run completed and at least one test failed), "passed"
(every test passed), "inconclusive" (timeout, no TRX, build failure or no test ran). A
defect counts as detected by a suite only if every one of its runs failed, survived
only if every run passed; anything else is reported as it is. A build failure is
reported as inconclusive, never as a detection. Nothing is retried."""
import difflib, hashlib, json, os, signal, subprocess, sys, time
import xml.etree.ElementTree as ET

catalog_path, side, copy, out = sys.argv[1:5]
only = set(sys.argv[5:])
# Absolute, because dotnet test runs in <copy> and resolves --results-directory there.
copy, out = os.path.abspath(copy), os.path.abspath(out)
catalog = json.load(open(catalog_path))
sides = catalog["sides"]
if side not in sides:
    sys.exit(f"unknown side {side}")
os.makedirs(out, exist_ok=False)
project = "src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj"
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
suites = {
    "close": "FullyQualifiedName~WebsocketClientLiteTest.ControlledCloseTests|FullyQualifiedName~WebsocketClientLiteTest.CloseBoundaryTests",
    "all": "FullyQualifiedName~WebsocketClientLiteTest",
}
# FULL_RUNS (default 1) repeats the whole test project; added after the first recorded run.
plan = [("close", 3), ("all", int(os.environ.get("FULL_RUNS", "1")))]


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


env = dict(os.environ, MSBUILDDISABLENODEREUSE="1")


def run(cmd, log, timeout):
    start = time.time()
    with open(log, "w") as f:
        f.write("$ " + " ".join(cmd) + "\n")
        f.flush()
        # A new session, so that a timeout can kill every process the command started.
        proc = subprocess.Popen(cmd, cwd=copy, stdout=f, stderr=subprocess.STDOUT, env=env, start_new_session=True)
        try:
            code = proc.wait(timeout=timeout)
            timed_out = False
        except subprocess.TimeoutExpired:
            try:
                os.killpg(proc.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            proc.wait()
            code, timed_out = None, True
    return {"command": cmd, "exit": code, "timed_out": timed_out, "seconds": round(time.time() - start, 2), "log": os.path.relpath(log, out)}


def trx(path):
    if not os.path.exists(path):
        return None
    root = ET.parse(path).getroot()
    results = root.findall(".//t:UnitTestResult", ns)
    failed, skipped = [], []
    for r in results:
        if r.get("outcome") == "NotExecuted":
            skipped.append(r.get("testName"))
        elif r.get("outcome") != "Passed":
            msg = r.find(".//t:Message", ns)
            first = (msg.text or "").strip().splitlines()[0] if msg is not None and msg.text else ""
            failed.append({"test": r.get("testName"), "outcome": r.get("outcome"), "message": first[:300]})
    return {"total": len(results), "passed": sum(r.get("outcome") == "Passed" for r in results),
            "skipped": skipped, "not_passed": failed}


def suite_runs(label):
    d = os.path.join(out, label)
    os.makedirs(d)
    build = run(["dotnet", "build", project, "-c", "Release", "-p:LibTfm=net11.0", "--nologo"], os.path.join(d, "build.log"), 600)
    entry = {"build": build, "suites": {}}
    if build["exit"] != 0:
        entry["outcome"] = "inconclusive: build failed"
        return entry
    for suite, count in plan:
        runs = []
        for i in range(1, count + 1):
            name = f"{suite}-{i}"
            r = run(["dotnet", "test", project, "-c", "Release", "-p:LibTfm=net11.0", "--no-build", "--nologo",
                     "--filter", suites[suite], "--logger", f"trx;LogFileName={name}.trx", "--results-directory", d],
                    os.path.join(d, name + ".log"), 600)
            t = trx(os.path.join(d, name + ".trx"))
            r["trx"] = t
            if r["timed_out"] or t is None or t["passed"] + len(t["not_passed"]) == 0:
                r["outcome"] = "inconclusive"
            elif r["exit"] != 0 and t["not_passed"]:
                r["outcome"] = "failed"
            elif r["exit"] == 0 and not t["not_passed"]:
                r["outcome"] = "passed"
            else:
                r["outcome"] = "inconclusive"
            runs.append(r)
        outcomes = {r["outcome"] for r in runs}
        verdict = "detected" if outcomes == {"failed"} else "survived" if outcomes == {"passed"} else "mixed: " + ",".join(r["outcome"] for r in runs)
        entry["suites"][suite] = {"verdict": verdict, "runs": runs}
    return entry


report = {"made": "2026-11-25", "side": side, "commit": sides[side]["commit"], "copy_name": os.path.basename(copy),
          "library_target": "net11.0", "configuration": "Release", "plan": plan, "suites": suites}
report["unchanged"] = suite_runs("unchanged")
if report["unchanged"].get("outcome") or any(s["verdict"] != "survived" for s in report["unchanged"]["suites"].values()):
    report["status"] = "invalid: the unchanged copy did not pass"
    json.dump(report, open(os.path.join(out, "results.json"), "w"), indent=1)
    sys.exit(1)
report["defects"] = []
for defect in [d for d in catalog["defects"] if not only or d["id"] in only]:
    path = os.path.join(copy, defect["file"])
    original = open(path, encoding="utf-8").read()
    mutated = original
    for edit in defect["edits"]:
        count = mutated.count(edit["before"])
        if count != 1:
            sys.exit(f"{defect['id']}: edit found {count} times: {edit['before']!r}")
        mutated = mutated.replace(edit["before"], edit["after"])
    source_hash = sha(path)
    open(path, "w", encoding="utf-8").write(mutated)
    diff = "".join(difflib.unified_diff(original.splitlines(True), mutated.splitlines(True), "a/" + defect["file"], "b/" + defect["file"]))
    open(os.path.join(out, defect["id"] + ".diff"), "w").write(diff)
    entry = {"id": defect["id"], "breaks": defect["breaks"], "kind": defect["kind"], "description": defect["description"],
             "source_sha256": source_hash, "mutant_sha256": sha(path)}
    try:
        entry.update(suite_runs(defect["id"]))
    finally:
        open(path, "w", encoding="utf-8").write(original)
    if sha(path) != source_hash:
        sys.exit(f"{defect['id']}: source not restored")
    report["defects"].append(entry)
    json.dump(report, open(os.path.join(out, "results.json"), "w"), indent=1)
report["status"] = "complete"
json.dump(report, open(os.path.join(out, "results.json"), "w"), indent=1)
for d in report["defects"]:
    print(d["id"], {k: v["verdict"] for k, v in d.get("suites", {}).items()}, d.get("outcome", ""))
