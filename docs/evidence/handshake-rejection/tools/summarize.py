# Handshake rejection (2026-11-11): per-test outcomes of every run of one or more batches.
# Usage: python3 -I summarize.py <runs-dir> <batch>... Reads run-NN.trx and run-NN.exit.
import sys, pathlib, re, xml.etree.ElementTree as ET
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = pathlib.Path(sys.argv[1])
for batch in sys.argv[2:]:
    d = root / batch
    runs = sorted(d.glob("run-*.exit"))
    per_test, failed_runs, lines = {}, 0, []
    for ex in runs:
        rid = ex.stem
        code = re.search(r"exit=(\d+)", ex.read_text()).group(1)
        ms = re.search(r"ms=(\d+)", ex.read_text()).group(1)
        trx = d / f"{rid}.trx"
        outcomes = {}
        if trx.exists():
            for r in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", ns):
                outcomes[r.get("testName")] = r.get("outcome")
        failed = sorted(n for n, o in outcomes.items() if o != "Passed")
        if code != "0": failed_runs += 1
        for n, o in outcomes.items():
            p = per_test.setdefault(n, {"Passed": 0, "other": 0})
            p["Passed" if o == "Passed" else "other"] += 1
        lines.append(f"{rid} exit={code} ms={ms} tests={len(outcomes)} not-passed={len(failed)}" + (": " + "; ".join(failed) if failed else ""))
    print(f"## {batch}: {len(runs)} runs, {failed_runs} with a non-zero exit")
    for l in lines: print("  " + l)
    for n in sorted(per_test):
        p = per_test[n]
        print(f"  passed {p['Passed']:2d} of {p['Passed'] + p['other']:2d}  {n}")
    print()
