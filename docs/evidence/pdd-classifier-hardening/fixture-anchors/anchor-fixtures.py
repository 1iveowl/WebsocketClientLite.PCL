"""Rewrites the Dafny line numbers in two Pdd fixture files as anchors relative to the
line of the enclosing declaration's body brace: {Module.Class.Member+N}. Run once on
2026-11-11 (session plan step S10, #136 review finding 7). Usage:
  python3 -I anchor-fixtures.py <repo> convert|check
convert writes the files; check renders the anchored files with the same anchors and
compares them with the original bytes from git HEAD."""
import json, re, subprocess, sys
repo, mode = sys.argv[1], sys.argv[2]
FILES = {"verification/Protocol.dfy": ("ProtocolCore", "Protocol"), "verification/Masking.dfy": ("MaskingCore", "Masking")}
FIXTURES = ["verification/tests/fixtures/mutation-cases.json", "verification/tests/fixtures/payload-diagnostics.json"]

def declarations(path):
    """(name, owner, first line, body open line, body close line) for each member with a body."""
    module, default = FILES[path]
    text = open(f"{repo}/{path}").read()
    lines = text.split("\n")
    owner, out = default, []
    for i, line in enumerate(lines):
        c = re.match(r"\s*class\s+(\w+)", line)
        if c: owner = c.group(1)
        d = re.match(r"\s*(?:(?:static|ghost|opaque)\s+)*(?:method|function|predicate|lemma)\s+(?:\{:[^}]*\}\s*)*(\w+)\s*[(<]", line)
        if not d: continue
        j = i
        while "{" not in re.sub(r"\{:[^}]*\}", "", lines[j]):
            j += 1
            if lines[j].strip() == "}": break  # a member without a body, such as the extern CopySlice
        if lines[j].strip() == "}": continue
        depth, k = 0, j
        for k in range(j, len(lines)):
            depth += lines[k].count("{") - lines[k].count("}")
            if depth == 0: break
        out.append((f"{module}.{owner}.{d.group(1)}", i + 1, j + 1, k + 1))
    return out

ANCHORS = {p: declarations(p) for p in FILES}
def token(path, line):
    for sym, first, open_, close in ANCHORS[path]:
        if first <= line <= close:
            off = line - open_
            return "{%s%s%d}" % (sym, "+" if off >= 0 else "-", abs(off))
    raise SystemExit(f"no declaration encloses {path}:{line}")

def anchor_text(s):
    out, current = [], None
    for line in s.split("\n"):
        m = re.match(r"(verification/\w+\.dfy)\((\d+),(\d+)\)(.*)$", line)
        if m:
            path = m.group(1)
            # The Masking.dfy case of mutation-cases.json copies the CloseStep lines of the
            # target case and changes only the file name; it is anchored to those lines.
            anchor_path = path if any(first <= int(m.group(2)) <= close for _, first, _, close in ANCHORS[path]) else "verification/Protocol.dfy"
            current = anchor_path
            line = f"{path}({token(anchor_path, int(m.group(2)))},{m.group(3)}){m.group(4)}"
        else:
            g = re.match(r"(\s*)(\d+) \|(.*)$", line)
            if g and current: line = f"{g.group(1)}{token(current, int(g.group(2)))} |{g.group(3)}"
        out.append(line)
    return "\n".join(out)

def render_text(s):
    def sub(m):
        sym, sign, n = m.group(1), m.group(2), int(m.group(3))
        for p in ANCHORS.values():
            for name, _, open_, _ in p:
                if name == sym: return str(open_ + (n if sign == "+" else -n))
        raise SystemExit(f"unknown anchor {sym}")
    return re.sub(r"\{(\w+\.\w+\.\w+)([+-])(\d+)\}", sub, s)

def walk(node, f):
    if isinstance(node, dict): return {k: walk(v, f) for k, v in node.items()}
    if isinstance(node, list): return [walk(v, f) for v in node]
    if isinstance(node, str): return f(node)
    return node

for fx in FIXTURES:
    original = subprocess.run(["git", "-C", repo, "show", f"HEAD:{fx}"], capture_output=True, text=True, check=True).stdout
    data = json.loads(original)
    if mode == "convert":
        text = json.dumps(walk(data, anchor_text), indent=2, ensure_ascii=False) + "\n"
        open(f"{repo}/{fx}", "w").write(text)
        print(f"converted {fx}")
    anchored = json.loads(open(f"{repo}/{fx}").read())
    same = walk(anchored, render_text) == data
    left = re.findall(r"\.dfy\((\d+),", json.dumps(anchored))
    print(f"{fx}: rendered with the current anchors equals HEAD: {same}; absolute Dafny locations left: {len(left)}")
    if not same or left: sys.exit(1)
for p, ds in ANCHORS.items():
    for d in ds: print(p, *d)
