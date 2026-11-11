# Removes exactly one guarded check from an isolated copy. Usage: defect.py <copy-root> <name>
import sys, pathlib
root, name = pathlib.Path(sys.argv[1]), sys.argv[2]
gen = root / "verification/generated/Masking.g.cs"
parse = root / "src/main/WebsocketClientLite/Helper/DataframeParsing.cs"
LIMIT_HV = "((length) <= (limit))"
BOUND_HV = "((length) <= (2147483647UL))"
LIMIT_STEP = ("if ((length) > ((limit) - (_0_prior))) {",
              "if (((state) != ((byte)(0))) && ((length) > ((limit) - (_0_prior)))) {")
edits = {
    # The configured frame size limit: HeaderValid's `length <= limit` and Step's check of a
    # first frame against the limit. Step's check of a continuation against the remaining
    # message budget stays.
    "size-limit": [(gen, LIMIT_HV, "(true)"), (gen,) + LIMIT_STEP],
    # The size limit as a whole: the configured limit and the 0x7fffffff bound.
    "size-bound": [(gen, LIMIT_HV, "(true)"), (gen, BOUND_HV, "(true)"), (gen,) + LIMIT_STEP],
    # The control frame length rule (125 bytes or less): PrefixValid's marker check,
    # HeaderValid's length check and Step's length check. The control FIN rule stays.
    "control-length": [
        (gen, "(((byte)((second) & ((byte)(127)))) <= ((byte)(125)))", "(true)"),
        (gen, "((length) <= (125UL))", "(true)"),
        (gen, "((!(fin)) || ((length) > (125U)))", "((!(fin)) || (false))")],
    # The bit-63 rule of HeaderValid.
    "high-bit": [(gen, "(((length) & (9223372036854775808UL)) == (0UL))", "(true)")],
    # Header-only rejection: the parser tries to read one byte past the header before it
    # throws the same protocol error. Everything it decides is unchanged.
    "reads-past-header": [(parse,
        "        if (status != 2)\n            throw new WebsocketClientLiteException(",
        "        if (status != 2)\n        {\n"
        "            try { await tcpConnection.ReadHeaderBytesAsync(1, ct).ConfigureAwait(false); }\n"
        "            catch (WebsocketClientLiteException) { }\n"
        "            throw new WebsocketClientLiteException(")],
}
for path, old, new in edits[name]:
    text = path.read_text()
    assert text.count(old) == 1, (name, old)
    text = text.replace(old, new)
    if name == "reads-past-header":
        marker = "or payload exceeds the configured maximum.\");\n"
        assert text.count(marker) == 1
        text = text.replace(marker, marker + "        }\n")
    path.write_text(text)
print("applied", name)
