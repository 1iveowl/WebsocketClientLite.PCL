"""Seeded decision-line defects for Experiment 1 (S6, 2026-11-11).

Usage: python3 -I decision_defects.py <catalog.json> <side> <copy> <out>

<copy> is a git archive copy of the commit the side names (the caller extracts it
and records the commit). For each defect of <side> in the catalog the script
applies every edit exactly once, builds the test project (Release, library
net10.0), runs the reconnect suite three times, the other existing tests once
and, where the side has them, the plain decision tests once, and restores the
file. The unchanged copy is run the same way first and must pass.

Outcomes per run: "failed" (the run completed and at least one test failed),
"passed" (every test passed), "inconclusive" (timeout, no TRX, build failure or
no test ran). A defect counts as detected by a suite only if every one of its
runs failed, survived only if every run passed; anything else is reported as it
is. Nothing is retried."""
import difflib, hashlib, json, os, subprocess, sys, time
import xml.etree.ElementTree as ET

catalog_path, side, copy, out = sys.argv[1:5]
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
    "reconnect": "FullyQualifiedName~WebsocketClientLiteTest.ReconnectTests",
    "plain": "FullyQualifiedName~WebsocketClientLiteTest.ReconnectDecisionTests",
    "other": "FullyQualifiedName!~WebsocketClientLiteTest.ReconnectTests&FullyQualifiedName!~WebsocketClientLiteTest.ReconnectDecisionTests",
}
plan = [("reconnect", 3), ("other", 1)] + ([("plain", 1)] if sides[side]["plain_tests"] else [])


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def run(cmd, log, timeout):
    start = time.time()
    with open(log, "w") as f:
        f.write("$ " + " ".join(cmd) + "\n")
        f.flush()
        try:
            code = subprocess.run(cmd, cwd=copy, stdout=f, stderr=subprocess.STDOUT, timeout=timeout).returncode
            timed_out = False
        except subprocess.TimeoutExpired:
            code, timed_out = None, True
    return {"command": cmd, "exit": code, "timed_out": timed_out, "seconds": round(time.time() - start, 2), "log": os.path.relpath(log, out)}


def trx(path):
    if not os.path.exists(path):
        return None
    root = ET.parse(path).getroot()
    results = root.findall(".//t:UnitTestResult", ns)
    failed = []
    for r in results:
        if r.get("outcome") != "Passed":
            msg = r.find(".//t:Message", ns)
            first = (msg.text or "").strip().splitlines()[0] if msg is not None and msg.text else ""
            failed.append({"test": r.get("testName"), "outcome": r.get("outcome"), "message": first[:300]})
    return {"total": len(results), "passed": sum(r.get("outcome") == "Passed" for r in results), "not_passed": failed}


def suite_runs(label):
    d = os.path.join(out, label)
    os.makedirs(d)
    build = run(["dotnet", "build", project, "-c", "Release", "-p:LibTfm=net10.0", "--nologo"], os.path.join(d, "build.log"), 600)
    entry = {"build": build, "suites": {}}
    if build["exit"] != 0:
        entry["outcome"] = "inconclusive: build failed"
        return entry
    for suite, count in plan:
        runs = []
        for i in range(1, count + 1):
            name = f"{suite}-{i}"
            r = run(["dotnet", "test", project, "-c", "Release", "-p:LibTfm=net10.0", "--no-build", "--nologo",
                     "--filter", suites[suite], "--logger", f"trx;LogFileName={name}.trx", "--results-directory", d],
                    os.path.join(d, name + ".log"), 600)
            t = trx(os.path.join(d, name + ".trx"))
            r["trx"] = t
            if r["timed_out"] or t is None or t["total"] == 0:
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


report = {"made": "2026-11-11", "side": side, "copy": copy, "commit": sides[side]["commit"],
          "library_target": "net10.0", "configuration": "Release", "plan": plan, "suites": suites}
report["unchanged"] = suite_runs("unchanged")
if report["unchanged"].get("outcome") or any(s["verdict"] != "survived" for s in report["unchanged"]["suites"].values()):
    report["status"] = "invalid: the unchanged copy did not pass"
    json.dump(report, open(os.path.join(out, "results.json"), "w"), indent=1)
    sys.exit(1)
report["defects"] = []
for defect in [d for d in catalog["defects"] if d["side"] == side]:
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
    entry = {"id": defect["id"], "rows": defect["rows"], "description": defect["description"],
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
