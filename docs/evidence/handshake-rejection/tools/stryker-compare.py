# Handshake rejection (2026-11-11). Usage: python3 -I stryker-compare.py <old-report> <new-report>
# Compare Stryker outcomes of two mutation reports by (file, line, column, mutator, replacement).
import json, sys
def load(p):
    r = json.load(open(p)); tests = {}
    for tf in (r.get('testFiles') or {}).values():
        for t in tf['tests']: tests[t['id']] = t['name']
    out = {}
    for f, d in r['files'].items():
        short = f.split('src/main/')[-1]
        for m in d['mutants']:
            loc = m['location']['start']
            key = (short, loc['line'], loc['column'], m['mutatorName'], m.get('replacement'))
            out[key] = (m['status'], sorted({tests.get(i, i) for i in m.get('killedBy', [])}), sorted({tests.get(i, i) for i in m.get('coveredBy', [])}))
    return out
a, b = load(sys.argv[1]), load(sys.argv[2])
tested = lambda s: s not in ('Ignored', 'CompileError')
print('old tested', sum(tested(v[0]) for v in a.values()), 'new tested', sum(tested(v[0]) for v in b.values()))
from collections import Counter
print('old', Counter(v[0] for v in a.values() if tested(v[0]))); print('new', Counter(v[0] for v in b.values() if tested(v[0])))
for k in sorted(set(a) | set(b), key=str):
    if a.get(k, ('absent',))[0] != b.get(k, ('absent',))[0]:
        print('CHANGED', k, a.get(k, ('absent',))[0], '->', b.get(k, ('absent',))[0], 'killedBy new:', b.get(k, (None, []))[1])
for k, v in b.items():
    if k[0].endswith('WebsocketConnectionHandler.cs') and k[1] in (93, 94, 95):
        print('LINE', k, v[0]); print('  killedBy', v[1]); print('  coveredBy', v[2])
