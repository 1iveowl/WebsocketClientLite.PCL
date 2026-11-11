# Review fixes (2026-11-11): removes or shifts one more guarded check in an isolated copy.
# Usage: defect-review.py <copy-root> <name>. Never run on the working checkout.
import sys, pathlib
root, name = pathlib.Path(sys.argv[1]), sys.argv[2]
gen = root / "verification/generated/Masking.g.cs"
tcp = root / "src/main/WebsocketClientLite/Service/TcpConnectionService.cs"
P = "WebsocketClientLite.VerifiedProtocol.Protocol."
# Known is not checked at all: unlike defect-part-2.py's unknown-opcodes, opcode 3 is known too.
KNOWN_PREFIX = (f"({P}Known(_0_op))", "(true)")
KNOWN_STEP = (f"(!({P}Known(op)))", "(false)")
# Step's idle rule also admits data opcodes 3 to 7.
IDLE = ("if (((((state) == ((byte)(0))) && ((op) != ((byte)(1)))) && ((op) != ((byte)(2)))) || ",
        "if ((((((state) == ((byte)(0))) && ((op) != ((byte)(1)))) && ((op) != ((byte)(2)))) && (((op) < ((byte)(3))) || ((op) > ((byte)(7))))) || ")
EOF_THROW = "                    throw new WebsocketClientLiteException(AbortedUnexpectedlyMessage);"
edits = {
    # PrefixValid and Step do not check Known for any opcode.
    "known-removed": [(gen,) + KNOWN_PREFIX, (gen,) + KNOWN_STEP],
    # known-removed, and Step's idle rule also admits data opcodes 3 to 7.
    "known-sequence-removed": [(gen,) + KNOWN_PREFIX, (gen,) + KNOWN_STEP, (gen,) + IDLE],
    # End of stream during a read throws an exception that is not WebsocketClientLiteException.
    "eof-as-other": [(tcp, EOF_THROW, '                    throw new InvalidOperationException("end of stream (eof-as-other)");')],
}
for path, old, new in edits[name]:
    text = path.read_text()
    assert text.count(old) == 1, (name, old)
    path.write_text(text.replace(old, new))
print("applied", name)
