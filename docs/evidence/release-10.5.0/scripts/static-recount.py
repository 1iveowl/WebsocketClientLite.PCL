#!/usr/bin/env python3
"""Static part of the 10.5 recount: counts and line numbers read from the tree in the cwd."""
import json, os, re, subprocess, collections, sys
def lines(p): return sum(1 for _ in open(p, encoding='utf-8-sig'))
def cs(root):
    n=0; files=0
    for d,_,fs in os.walk(root):
        if any(x in d.split(os.sep) for x in ('bin','obj')): continue
        for f in fs:
            if f.endswith('.cs'): n+=lines(os.path.join(d,f)); files+=1
    return n, files
def find(path, pattern, after=None, nth=1):
    L=open(path).read().split('\n'); start=0
    if after: start=next(i for i,l in enumerate(L) if after in l)
    hits=[i+1 for i,l in enumerate(L) if i>=start and re.search(pattern,l)]
    return hits
out=[]
def put(k,v): out.append(f"{k}\t{v}")
for f in ['verification/Protocol.dfy','verification/Masking.dfy','verification/generated/Masking.g.cs']: put(f'lines {f}', lines(f))
for r in ['src/main/WebsocketClientLite','src/interface/IWebsocketClientLite','src/main/WebsocketClientLiteTest','tools/Pdd','tools/Pdd.Tests']:
    n,c=cs(r); put(f'C# lines {r}', f'{n} in {c} files (*.cs, bin/obj excluded)')
req=json.load(open('verification/requirements.json'))['requirements']
put('requirements (verification/requirements.json)', len(req))
k=collections.Counter(o['kind'] for r in req for o in r['obligations']); put('obligations by kind', dict(sorted(k.items())))
d=collections.Counter(o.get('delivery') for r in req for o in r['obligations']); put('obligations by delivery', dict(sorted(d.items(), key=str)))
put('obligations total', sum(k.values()))
put('boundaries (verification/boundaries.json)', len(json.load(open('verification/boundaries.json'))['boundaries']))
m=[x for f in ['close','payload'] for x in json.load(open(f'verification/mutations/{f}.json'))['mutants']]; put('Dafny mutants in catalogs', len(m))
st=json.load(open('verification/stryker-close-dispositions.json'))
put('Stryker must-kill (required_killed)', len(st['required_killed']))
put('Stryker dispositions', f"{len(st['mutants'])}: {dict(collections.Counter(x['classification'] for x in st['mutants']))}")
P='verification/Protocol.dfy'
put('HeaderValid RFC citation line', find(P, r'// RFC 6455 sections 5\.2 and 5\.5, mixed with library policy\. Section 5\.2'))
put('HeaderValid', find(P, r'static function HeaderValid\('))
put('length rule (marker 126/127 bounds)', find(P, r'else if marker == 126 then 126 <= length <= 65535 else length >= 65536'))
put('DecodeHeader', find(P, r'static method DecodeHeader\('))
put("decoder's status == 2 clause", find(P, r'ensures status == 2 ==> HeaderValid\(first, second, length, limit\) && available >= headerSize - 2'))
put('HeaderRoundTrip', find(P, r'HeaderRoundTrip\('))
put('Outgoing', find(P, r'static method Outgoing\('))
T='src/main/WebsocketClientLiteTest/PayloadParsingTests.cs'
put('81 7E 00 7D row', find(T, r'new byte\[\] \{ 0x81, 126, 0, 125 \}'))
put('RejectsMalformedFrames', find(T, r'public async Task RejectsMalformedFrames\('))
put('DataframeParsing.cs Protocol.Admit calls (Admit calls DecodeHeader)', find('src/main/WebsocketClientLite/Helper/DataframeParsing.cs', r'Protocol\.Admit\('))
put('Admit calls DecodeHeader (Protocol.dfy)', find(P, r'DecodeHeader\(first, second'))
put('stricter validation sentence', [f"CHANGELOG.md:{n}" for n in find('CHANGELOG.md', r'Stricter validation may reject')] + [f"docs/protocol-verification.md:{n}" for n in find('docs/protocol-verification.md', r'stricter validation can reject')] + [f"README.md:{n}" for n in find('README.md', r'(?i)stricter')])
put('executable specifications sentence', [f"verification/README.md:{n}" for n in find('verification/README.md', r'Pure acceptance functions are executable specifications')])
def body(text):
    i=text.index('static function HeaderValid('); j=text.index('{', i); depth=0
    for k in range(j, len(text)):
        if text[k]=='{': depth+=1
        elif text[k]=='}':
            depth-=1
            if depth==0: return text[j:k+1]
old=subprocess.check_output(['git','show','8f8f207:verification/Protocol.dfy'], text=True)
put('HeaderValid body unchanged since 8f8f207', body(old)==body(open(P).read()))
put('HEAD', subprocess.check_output(['git','rev-parse','HEAD'], text=True).strip())
print('\n'.join(out))
