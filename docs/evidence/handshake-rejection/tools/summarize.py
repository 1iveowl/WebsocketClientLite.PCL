# Handshake rejection (2026-11-11, review fix 2026-11-12): per-test outcomes of every run
# of one or more batches.
# Usage: python3 -I summarize.py <runs-dir> <batch>... Reads run-NN.trx and run-NN.exit.
#
# A run without a .trx (testhost crash, abort, no build) or a test missing from a run's
# .trx is inconclusive for that test, never passed or failed: it is shown on the run's
# line and counted under "inconclusive" for every test the batch expects. A trx outcome
# other than Passed or Failed (NotExecuted, Timeout, ...) is inconclusive too. The
# expected tests are every test name that any run of the batch reported.
import sys, pathlib, re, xml.etree.ElementTree as ET
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = pathlib.Path(sys.argv[1])
for batch in sys.argv[2:]:
    d = root / batch
    runs = sorted(d.glob("run-*.exit"))
    results, failed_runs, missing_trx = [], 0, 0
    for ex in runs:
        rid = ex.stem
        text = ex.read_text()
        code = re.search(r"exit=(\d+)", text).group(1)
        ms = re.search(r"ms=(\d+)", text).group(1)
        trx = d / f"{rid}.trx"
        outcomes = None
        if trx.exists():
            outcomes = {}
            for r in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", ns):
                outcomes[r.get("testName")] = r.get("outcome")
        else:
            missing_trx += 1
        if code != "0": failed_runs += 1
        results.append((rid, code, ms, outcomes))
    expected = sorted({n for *_, o in results if o for n in o})
    per_test = {n: {"Passed": 0, "Failed": 0, "inconclusive": 0} for n in expected}
    lines = []
    for rid, code, ms, outcomes in results:
        if outcomes is None:
            for n in expected: per_test[n]["inconclusive"] += 1
            lines.append(f"{rid} exit={code} ms={ms} NO-TRX: inconclusive for all {len(expected)} expected tests")
            continue
        failed = sorted(n for n, o in outcomes.items() if o == "Failed")
        other = sorted(f"{n} ({o})" for n, o in outcomes.items() if o not in ("Passed", "Failed"))
        absent = [n for n in expected if n not in outcomes]
        for n in expected:
            o = outcomes.get(n)
            per_test[n][o if o in ("Passed", "Failed") else "inconclusive"] += 1
        line = f"{rid} exit={code} ms={ms} tests={len(outcomes)} failed={len(failed)}" + (": " + "; ".join(failed) if failed else "")
        if other: line += f" | inconclusive outcome: {'; '.join(other)}"
        if absent: line += f" | absent from trx, inconclusive: {'; '.join(absent)}"
        lines.append(line)
    print(f"## {batch}: {len(runs)} runs, {failed_runs} with a non-zero exit, {missing_trx} without a trx")
    for l in lines: print("  " + l)
    if runs and not expected: print("  no run reported any test: every run is inconclusive")
    for n in expected:
        p = per_test[n]
        print(f"  passed {p['Passed']:2d}, failed {p['Failed']:2d}, inconclusive {p['inconclusive']:2d} of {sum(p.values()):2d}  {n}")
    print()
