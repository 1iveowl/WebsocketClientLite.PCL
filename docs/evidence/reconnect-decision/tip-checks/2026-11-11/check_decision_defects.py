"""Check of decision_defects.py's run() and trx() after the M3 review fix (2026-11-11).
Usage: python3 -I check_decision_defects.py <decision_defects.py> <empty work directory>
Loads only the two functions and the env and ns assignments, then checks:
1. a command that starts a background child and outlives its timeout is killed with
   its child (the process group), and the run is reported as timed out;
2. a TRX with one Passed and one NotExecuted result reports the skipped test as
   skipped, not as a failure."""
import ast, os, signal, subprocess, sys, time
import xml.etree.ElementTree as ET
script, work = sys.argv[1:3]
tree = ast.parse(open(script).read())
ns = {"os": os, "signal": signal, "subprocess": subprocess, "time": time, "ET": ET, "copy": work, "out": work}
for n in tree.body:
    if (isinstance(n, ast.FunctionDef) and n.name in ("run", "trx")) or \
       (isinstance(n, ast.Assign) and getattr(n.targets[0], "id", None) in ("env", "ns")):
        exec(compile(ast.Module([n], []), script, "exec"), ns)
log = os.path.join(work, "timeout.log")
r = ns["run"](["bash", "-c", "sleep 300 & echo child $!; wait"], log, 2)
child = open(log).read().split()[-1]
time.sleep(0.5)
alive = os.path.exists(f"/proc/{child}") and open(f"/proc/{child}/stat").read().split()[2] != "Z"
print("1. run:", {k: r[k] for k in ("exit", "timed_out")}, "child alive after timeout:", alive)
trx = os.path.join(work, "skip.trx")
open(trx, "w").write('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                     '<UnitTestResult testName="a" outcome="Passed"/><UnitTestResult testName="b" outcome="NotExecuted"/>'
                     '</Results></TestRun>')
t = ns["trx"](trx)
print("2. trx:", t)
ok = r["timed_out"] and not alive and t["skipped"] == ["b"] and t["not_passed"] == [] and t["passed"] == 1
print("result:", "passed" if ok else "failed")
sys.exit(0 if ok else 1)
