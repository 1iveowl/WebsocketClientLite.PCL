# Lists each TRX in part-2/ with its counts and every failing case with its first message line.
# Usage: python3 -I summarize-part-2.py <part-2-directory>
import sys, pathlib, xml.etree.ElementTree as ET
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
for trx in sorted(pathlib.Path(sys.argv[1]).glob("*/*.trx")):
    root = ET.parse(trx).getroot()
    results = root.findall(".//t:UnitTestResult", ns)
    failed = [r for r in results if r.get("outcome") != "Passed"]
    print(f"## {trx.parent.name}/{trx.stem}: {len(results) - len(failed)} of {len(results)} passed")
    for r in sorted(failed, key=lambda r: r.get("testName")):
        msg = r.find(".//t:Message", ns)
        first = (msg.text or "").strip().splitlines()[0] if msg is not None and msg.text else ""
        print(f"- {r.get('outcome')}: {r.get('testName').replace('WebsocketClientLiteTest.', '')} -- {first[:220]}")
