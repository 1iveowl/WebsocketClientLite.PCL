# Handshake rejection (2026-11-11): removes `throw handshakeException;` in an isolated copy,
# as Stryker's statement mutation at WebsocketConnectionHandler.cs line 95 does.
# Usage: defect.py <copy-root> <name>. Never run on the working checkout.
import sys, pathlib
root, name = pathlib.Path(sys.argv[1]), sys.argv[2]
handler = root / "src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs"
edits = {
    # Stryker mutant 573 in the S4 and review captures: the statement becomes `;`.
    "throw-removed": [(handler, "            throw handshakeException;\n", "            ;\n")],
}
for path, old, new in edits[name]:
    text = path.read_text()
    assert text.count(old) == 1, (name, old)
    lines = text.splitlines(keepends=True)
    assert lines[94] == old, ("line 95 is not the throw", lines[94])
    path.write_text(text.replace(old, new))
print("applied", name)
