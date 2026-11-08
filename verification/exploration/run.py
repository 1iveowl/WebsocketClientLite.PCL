#!/usr/bin/env python3
"""Manual bounded baseline and isolated capacity-two replay; no CI integration."""
import hashlib
import json
from pathlib import Path
import resource
import shutil
import subprocess
import tempfile
import time

root = Path(__file__).resolve().parents[2]
out = root / 'docs/evidence/p3-f'
out.mkdir(parents=True, exist_ok=True)
resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
records = []

def run(name, command, cwd=root, expected=0):
    start = time.monotonic()
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True, timeout=300)
    (out / name).write_text(result.stdout + result.stderr)
    records.append(dict(log=name, command=command, exit=result.returncode, seconds=time.monotonic()-start))
    (out / 'commands.json').write_text(json.dumps(records, indent=2) + '\n')
    if expected == 0:
        assert result.returncode == 0, name
    else:
        assert result.returncode != 0 and '"error":"Overlapping frame delegates"' in result.stdout, name
    rows = [json.loads(line) for line in result.stdout.splitlines() if line.startswith('{')]
    if name.startswith('baseline-'):
        assert rows[0]['candidates'] == 5040 and rows[0]['discarded'] == 4830
        assert rows[-1]['passed'] == 210 and len({r['order'] for r in rows[1:-1]}) == 210
    if expected != 0:
        failure = next(r for r in rows if r.get('result') == 'fail')
        assert failure['order'] == 'DCPRHXA'
        trace = failure['trace']
        causal = ['F', 'write:1:enter', 'D', 'C', 'single:OperationCanceledException', 'P', 'write:9:enter', 'R']
        assert trace[:len(causal)] == causal, trace
    return result.stdout

files = subprocess.check_output(['git', 'ls-files', 'src/main/WebsocketClientLite', 'src/interface', 'verification/generated'], cwd=root, text=True).splitlines()
files += ['verification/exploration/' + x for x in ['Program.cs', 'Exploration.csproj', 'run.py']]
def hashes():
    return {p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in files}
before = hashes()
identity = dict(base=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(),
                sourceBefore=before, sdk=subprocess.check_output(['dotnet', '--version'], text=True).strip())
run('build.log', ['dotnet', 'build', 'verification/exploration/Exploration.csproj', '-c', 'Release'])
for i in range(1, 4):
    run(f'baseline-{i}.jsonl', ['dotnet', 'verification/exploration/bin/Release/net10.0/WebsocketClientLiteTest.dll'])
with tempfile.TemporaryDirectory(prefix='p3-f-defect-') as directory:
    copy = Path(directory)
    for p in files:
        target = copy / p
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(root / p, target)
    target = copy / 'src/main/WebsocketClientLite/Service/TcpConnectionService.cs'
    original = target.read_text()
    old = 'private readonly SemaphoreSlim _writeGate = new(1, 1);'
    new = 'private readonly SemaphoreSlim _writeGate = new(2, 2);'
    assert original.count(old) == 1
    target.write_text(original.replace(old, new))
    identity['defect'] = dict(path=str(target.relative_to(copy)), old=old, new=new,
                              sha256=hashlib.sha256(target.read_bytes()).hexdigest())
    run('defect-build.log', ['dotnet', 'build', 'verification/exploration/Exploration.csproj', '-c', 'Release'], copy)
    for i in range(1, 4):
        run(f'defect-{i}.log', ['dotnet', 'verification/exploration/bin/Release/net10.0/WebsocketClientLiteTest.dll', 'DCPRHXA'], copy, expected=1)
identity['sourceAfter'] = hashes()
assert identity['sourceAfter'] == before
(out / 'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
print(json.dumps(records, indent=2))
