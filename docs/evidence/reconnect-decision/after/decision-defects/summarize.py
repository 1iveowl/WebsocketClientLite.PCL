"""Summarize the seeded decision-defect runs (S6, 2026-11-11) as a Markdown table.
Usage: python3 -I summarize.py <results.json>...
A suite's verdict is "detected" when every run failed, "survived" when every run
passed, and the run outcomes otherwise. Failing tests are listed with the first
line of their message."""
import json, sys

print("| Side | Defect | Rows | Reconnect suite (3 runs) | Other existing tests (1 run) | Plain tests (1 run) | Failing tests |")
print("| --- | --- | --- | --- | --- | --- | --- |")
for path in sys.argv[1:]:
    r = json.load(open(path))
    entries = [("unchanged copy", {"rows": [], **r["unchanged"]})] + [(d["id"], d) for d in r.get("defects", [])]
    for name, e in entries:
        suites = e.get("suites", {})
        def verdict(s):
            if s not in suites:
                return "none at this commit" if s == "plain" else "not run"
            v = suites[s]["verdict"]
            return "passed" if name == "unchanged copy" and v == "survived" else v
        failing = sorted({f"`{f['test'].split('WebsocketClientLiteTest.')[-1]}`: {f['message']}"
                          for s in suites.values() for run in s["runs"] for f in (run["trx"] or {}).get("not_passed", [])})
        shown = "; ".join(failing[:3]) + (f"; and {len(failing) - 3} more" if len(failing) > 3 else "")
        rows = ", ".join(str(x) for x in e["rows"])
        build = "" if e["build"]["exit"] == 0 else " (build failed: inconclusive)"
        print(f"| {r['side']} | {name}{build} | {rows} | {verdict('reconnect')} | {verdict('other')} | {verdict('plain')} | {shown} |")
