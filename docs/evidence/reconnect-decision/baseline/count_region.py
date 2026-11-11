"""Count the stateful region of ClientWebSocketRx.cs for Experiment 1 (S5, 2026-11-11).
Usage: python3 -I count_region.py <path to ClientWebSocketRx.cs>
Region = fields 24-26, the Observable.Create body of WebsocketConnectWithStatusObservable
(248-307), Dispose(bool) (327-346) and DisposeAsync (366-379). Ranges are checked against
anchor text so a shifted file fails instead of being miscounted."""
import hashlib, re, sys
path = sys.argv[1]
raw = open(path, 'rb').read()
lines = raw.decode('utf-8').split('\n')
anchors = {24: 'private readonly object _lifecycleGate', 26: 'private object? _generation',
           248: 'return Observable.Create<(IDataframe? dataframe, ConnectionStatus state)>',
           307: '});', 327: 'protected virtual void Dispose(bool disposing)', 346: '}',
           366: 'public async ValueTask DisposeAsync()', 379: '}'}
for n, text in anchors.items():
    assert text in lines[n - 1], f'anchor {n} moved: {lines[n - 1]!r}'
parts = {'fields': (24, 26), 'connect observable': (248, 307), 'Dispose(bool)': (327, 346), 'DisposeAsync': (366, 379)}
state = re.compile(r'_lifecycleGate|_disposed|_generation|\bgeneration\b')
print('file', path)
print('sha256', hashlib.sha256(raw).hexdigest())
tot = {'physical': 0, 'code': 0, 'state': 0, 'locks': 0}
for name, (a, b) in parts.items():
    seg = [(i, lines[i - 1]) for i in range(a, b + 1)]
    code = [(i, l) for i, l in seg if l.strip() and not l.strip().startswith('//')]
    st = [(i, l) for i, l in code if state.search(l)]
    locks = [i for i, l in code if 'lock (_lifecycleGate)' in l]
    print(f'{name}: lines {a}-{b}, physical {len(seg)}, code {len(code)}, lifecycle-state lines {len(st)}, lock sites {locks}')
    for i, l in st:
        print(f'    {i:4}: {l.strip()}')
    tot['physical'] += len(seg); tot['code'] += len(code); tot['state'] += len(st); tot['locks'] += len(locks)
print('total', tot)
all_locks = [i + 1 for i, l in enumerate(lines) if 'lock (' in l]
print('every lock statement in file', all_locks)
