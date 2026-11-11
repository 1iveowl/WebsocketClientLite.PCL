# S4 (part 2): removes or shifts exactly one guarded check in an isolated copy.
# Usage: defect-part-2.py <copy-root> <name>. Never run on the working checkout.
import sys, pathlib
root, name = pathlib.Path(sys.argv[1]), sys.argv[2]
gen = root / "verification/generated/Masking.g.cs"
tcp = root / "src/main/WebsocketClientLite/Service/TcpConnectionService.cs"
P = "WebsocketClientLite.VerifiedProtocol.Protocol."
RSV = ("(((byte)((first) & ((byte)(112)))) == ((byte)(0)))", "(((byte)((first) & ((byte)(64)))) == ((byte)(0)))")
KNOWN_PREFIX = (f"({P}Known(_0_op))", f"(({P}Known(_0_op)) || ((_0_op) != ((byte)(3))))")
KNOWN_STEP = (f"(!({P}Known(op)))", f"(!(({P}Known(op)) || ((op) != ((byte)(3)))))")
IDLE = ("if (((((state) == ((byte)(0))) && ((op) != ((byte)(1)))) && ((op) != ((byte)(2)))) || ",
        "if ((((((state) == ((byte)(0))) && ((op) != ((byte)(1)))) && ((op) != ((byte)(2)))) && (((op) < ((byte)(4))) || ((op) > ((byte)(7))))) || ")
LIMIT_HV = "((length) <= (limit))"
LIMIT_STEP = "if ((length) > ((limit) - (_0_prior))) {"
EOF_THROW = "                    throw new WebsocketClientLiteException(AbortedUnexpectedlyMessage);"
edits = {
    # RSV2 and RSV3: PrefixValid checks only RSV1 (mask 0x40 instead of 0x70).
    "rsv2-rsv3": [(gen,) + RSV],
    # Reserved opcodes other than 3: PrefixValid and Step treat every opcode but 3 as known.
    # Opcode 3 stays refused, so the existing row keeps passing.
    "unknown-opcodes": [(gen,) + KNOWN_PREFIX, (gen,) + KNOWN_STEP],
    # unknown-opcodes, and Step's idle rule also admits data opcodes 4 to 7.
    "unknown-opcodes-sequence": [(gen,) + KNOWN_PREFIX, (gen,) + KNOWN_STEP, (gen,) + IDLE],
    # The configured limit is off by one upwards for a single frame: HeaderValid accepts
    # limit + 1, and Step accepts limit + 1 for a first frame. Continuations keep their budget.
    "limit-plus-one": [(gen, LIMIT_HV, "((length) <= ((limit) + (1UL)))"),
        (gen, LIMIT_STEP, "if (((state) == ((byte)(0))) ? ((length) > ((limit) + (1U))) : ((length) > ((limit) - (_0_prior)))) {")],
    # The configured limit is exclusive in HeaderValid: a frame of exactly the limit is refused.
    "limit-strict": [(gen, LIMIT_HV, "((length) < (limit))")],
    # End of stream during a read is taken as cancellation: the read returns no data and
    # the parser returns null instead of failing.
    "eof-as-cancel": [(tcp, EOF_THROW, "                    return false;")],
    # End of stream during a read is taken as a complete read: the buffer keeps whatever it
    # held before, and the parser decides on bytes that never arrived.
    "eof-as-data": [(tcp, EOF_THROW, "                    break;")],
}
for path, old, new in edits[name]:
    text = path.read_text()
    assert text.count(old) == 1, (name, old)
    path.write_text(text.replace(old, new))
print("applied", name)
