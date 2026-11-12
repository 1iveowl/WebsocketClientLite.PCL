"""Count the stateful region of ClientWebSocketRx.cs after the S6 change (2026-11-11),
in the same way as S5's baseline/count_region.py, and the new files' lines.
Usage: python3 -I count_region_after.py <repository root>
Region = the fields (25-27), Advance (159-167, new), the Observable.Create body of
WebsocketConnectWithStatusObservable (259-320), Dispose(bool) (340-360) and
DisposeAsync (380-393). Ranges are checked against anchor text, so a shifted file
fails instead of being miscounted. "Code" excludes blank and comment lines.
Two lifecycle-state counts are printed: S5's pattern unchanged, and the same pattern
widened to the after names (_lifecycle, Advance, ReconnectDecision), which is the
comparable one because _disposed and _generation no longer exist."""
import hashlib, os, re, sys
root = sys.argv[1]
path = os.path.join(root, 'src/main/WebsocketClientLite/ClientWebSocketRx.cs')
raw = open(path, 'rb').read()
lines = raw.decode('utf-8').split('\n')
anchors = {25: 'private readonly object _lifecycleGate', 27: 'private LifecycleState _lifecycle;',
           159: '// The lifecycle decision is the pure ReconnectDecision.Decide', 162: 'private LifecycleEffect Advance(', 167: '}',
           259: 'return Observable.Create<(IDataframe? dataframe, ConnectionStatus state)>', 320: '});',
           340: 'protected virtual void Dispose(bool disposing)', 360: '}',
           380: 'public async ValueTask DisposeAsync()', 393: '}'}
for n, text in anchors.items():
    assert text in lines[n - 1], f'anchor {n} moved: {lines[n - 1]!r}'
parts = {'fields': (25, 27), 'Advance (new)': (159, 167), 'connect observable': (259, 320),
         'Dispose(bool)': (340, 360), 'DisposeAsync': (380, 393)}
s5_state = re.compile(r'_lifecycleGate|_disposed|_generation|\bgeneration\b')
after_state = re.compile(r'_lifecycleGate|_lifecycle\b|_disposed|_generation|\bgeneration\b|Advance\(|ReconnectDecision\.')
def is_code(l):
    t = l.strip()
    return t and not t.startswith('//')
print('file', path)
print('sha256', hashlib.sha256(raw).hexdigest())
tot = {'physical': 0, 'code': 0, 'state_s5_pattern': 0, 'state_after_pattern': 0, 'locks': 0}
for name, (a, b) in parts.items():
    seg = [(i, lines[i - 1]) for i in range(a, b + 1)]
    code = [(i, l) for i, l in seg if is_code(l)]
    st5 = [(i, l) for i, l in code if s5_state.search(l)]
    sta = [(i, l) for i, l in code if after_state.search(l)]
    locks = [i for i, l in code if 'lock (_lifecycleGate)' in l]
    print(f'{name}: lines {a}-{b}, physical {len(seg)}, code {len(code)}, lifecycle-state lines {len(st5)} (S5 pattern) / {len(sta)} (after pattern), lock sites {locks}')
    for i, l in sta:
        print(f'    {i:4}: {l.strip()}')
    tot['physical'] += len(seg); tot['code'] += len(code); tot['state_s5_pattern'] += len(st5)
    tot['state_after_pattern'] += len(sta); tot['locks'] += len(locks)
print('total', tot)
print('every lock statement in file', [i + 1 for i, l in enumerate(lines) if 'lock (' in l])
for rel in ['src/main/WebsocketClientLite/Helper/ReconnectDecision.cs', 'src/main/WebsocketClientLiteTest/ReconnectDecisionTests.cs']:
    f = os.path.join(root, rel)
    text = open(f, 'rb').read()
    ls = text.decode('utf-8').rstrip('\n').split('\n')
    code = [l for l in ls if is_code(l) and not l.strip().startswith('///')]
    print(f'{rel}: sha256 {hashlib.sha256(text).hexdigest()}, physical {len(ls)}, code (no blank, // or /// lines) {len(code)}')
