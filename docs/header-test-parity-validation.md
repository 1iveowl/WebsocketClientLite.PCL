# Header test parity: validation

**Made on 2026-11-11.** This note checks whether the header tests back what the
documents say about header validation, and records each test failing against code
that lacks the behaviour it tests. It covers steps S3 and S4 of
`docs/pdd-10.3-session-plan.md` (Appendix C, W1 and W2). The production code does not
change. The proofs are unchanged; these runs test the tests.

## The claims

The documents make these claims about incoming frame headers:

- `docs/protocol-verification.md`, line 120: invalid frames fail before payload
  allocation; masked server frames, unsupported reserved bits and opcodes, non-minimal
  lengths, invalid control frames and oversized lengths are rejected, and protocol
  constraints and resource limits are checked before the payload is read.
- `verification/README.md`, line 176: the implementation rejects malformed headers,
  non-minimal lengths and unsupported opcodes and RSV bits, and size checks run before
  that frame's payload is allocated or read.
- `docs/dafny-pdd-plan.md`, line 57: test every header truncation, the boundaries 0,
  125, 126, 65535 and 65536, the configured limit and its successor, non-minimal
  encodings, reserved opcodes, RSV bits, masked server frames, fragmented controls and
  the 64-bit high bit.

## Method

Each failing-first run used its own `git archive` copy of a commit, never the working
checkout. In a copy of the current source, at most one guarded check was removed or
shifted, in `verification/generated/Masking.g.cs` where production runs it, or in
`TcpConnectionService.cs` for end of stream. Where `a779602` (release 9.2.0, the parser
before the proof work) lacked a behaviour, the tests also ran there. Every run is kept
with its log, TRX file, diff and source hashes, and none was repeated:

- part 1 (S3): [evidence/header-test-parity/](evidence/header-test-parity/README.md),
  copies of `fe0bd48` and `58bc568`;
- part 2 (S4): [evidence/header-test-parity/part-2/](evidence/header-test-parity/part-2/README.md),
  copies of `bcdcda6`.

A test that sends a header without its payload also passes when the parser accepts the
header and then reaches the end of the stream. Every rejection test here therefore
sends the payload its header announces, so that an accepting parser returns a frame and
the test fails on its own (lesson 5 of the plan).

## Claims and the tests that back them

| # | Claim | Test that backs it | Failing first | What stays unbacked |
| --- | --- | --- | --- | --- |
| 1 | Non-minimal lengths are rejected | `PayloadParsingTests.RejectsMalformedFrames`, the 16-bit and 64-bit non-minimal rows | Replays 2 and 3 on `a779602` (`docs/header-length-validation.md`); part 2's `a779602` replay again | Nothing |
| 2 | Invalid headers fail before the payload is read or allocated | `ParserRetentionTests.InvalidHeadersRejectBeforePayloadRead`, 20 rows: every invalid header row, each with its payload on the stream. The bytes read and the read attempts must equal the header bytes the decision needs. Also the successor rows of claim 6 | `reads-past-header` fails all 20 rows (part 1 for the first 9, part 2 for all 20) and every successor row of claim 6. On `a779602` the control-length row reads 4 bytes, not 2 | Allocation is not observed. The tests show that no payload read is attempted; that no buffer is allocated follows from the code, where the buffer is allocated in the payload read |
| 3 | Masked server frames are rejected | `PayloadParsingTests.RejectsMaskedServerFrame`, and its row in the claim 2 theory | `a779602` accepted it (part 1 and part 2) | Nothing |
| 4 | Unsupported RSV bits and opcodes are rejected | `RejectsMalformedFrames` and the claim 2 theory: RSV1, RSV2, RSV3, opcodes 3 to 7 and 11 to 15, each in both | `a779602` accepts all 13 values. `rsv2-rsv3` fails the RSV2 and RSV3 rows. `unknown-opcodes` fails the rows for opcodes 11 to 15. `unknown-opcodes-sequence` fails the rows for 4 to 7 as well | The opcode check (`Known`) alone, for opcodes 3 to 7: without it, `Step`'s sequence rule still refuses a data opcode other than text or binary at idle, and other than continuation within a message. The rows are combined coverage, and a comment says so |
| 5 | Invalid control frames are rejected | `Parse_OversizedControlFrame_Throws` (with its payload since part 1), `Parse_FragmentedControlFrame_Throws`, and both rows in the claim 2 theory | Part 1: `control-length` fails the oversized test and its theory row. The fragmented row fails under `reads-past-header` | The control FIN rule alone: no copy removes it, and `a779602` already had it. `Parse_FragmentedControlFrame_Throws` sends a frame of length 0, so an accepting parser would return a frame and fail it, but that was not run |
| 6 | The configured limit and its successor | `ParserRetentionTests.ConfiguredLimitAcceptsLimitAndRejectsSuccessorFromHeader`, limits 1, 100, 125, 200, 65535 and 65536: a frame of exactly the limit is read in full, and one byte more is refused after its header | `size-limit` and `limit-plus-one` fail all 6 rows on the successor. **Under `limit-plus-one`, every test that existed before S4 passes.** `limit-strict` fails all 6 rows on the limit itself, and also the existing `AccumulatedLimitRejectsBeforePayloadRead` | `a779602` had the same inclusive limit, so it cannot fail these rows. Only an inclusive comparison is tested, at six limits |
| 7 | Every header truncation | `PayloadParsingTests.TruncatedHeadersNeverProduceFrames`, now a theory over a 4-byte (16-bit) and a 10-byte (64-bit) header. Every proper prefix must end in the end-of-stream error, and every prefix that does not is listed | `eof-as-cancel` fails both rows at every length. `eof-as-data` fails both rows; the 16-bit header produces a frame at lengths 1, 2 and 3 | No change in today's code affects a 16-bit truncation alone: the reader is shared, and the header size is proved. The row's own lengths 2 and 3 fail in both copies. A 2-byte header's truncations are lengths 0 and 1 of both rows |
| 8 | Boundaries 0, 125, 126, 65535 and 65536 decode | `PayloadParsingTests.ParsesCanonicalServerLengths` | Not run: W2 lists no gap here | Not shown failing first |

The 64-bit high bit, which `docs/dafny-pdd-plan.md` also lists, is the high-bit row of
`RejectsMalformedFrames` and of the claim 2 theory. Part 1 shows it is combined
coverage: any length with bit 63 set also exceeds `int.MaxValue`, and removing either
the bit-63 rule or the size limit leaves the row rejected by the other.

## What changed in the tests

Part 1 (S3, `58bc568`):

- `Parse_FrameExceedingMaxFrameSize_Throws` and `Parse_OversizedControlFrame_Throws`
  send their 200 payload bytes. Before, both passed with their check removed.
- The high-bit row of `RejectsMalformedFrames` has a comment: combined coverage.
- New theory `ParserRetentionTests.InvalidHeadersRejectBeforePayloadRead`.

Part 2 (S4, `bcdcda6`):

- `RejectsMalformedFrames` and `InvalidHeadersRejectBeforePayloadRead` have a row for
  RSV2, RSV3, opcodes 4 to 7 and opcodes 11 to 15, owner decision 4. The rows for
  opcodes 4 to 7 have a comment: combined coverage with the sequence rule.
- New theory `ParserRetentionTests.ConfiguredLimitAcceptsLimitAndRejectsSuccessorFromHeader`.
- `TruncatedHeadersNeverProduceFrames` is a theory over a 16-bit and a 64-bit header,
  and it now requires the end-of-stream error, compared through the shared constant
  `TcpConnectionService.AbortedUnexpectedlyMessage`. Before, any
  `WebsocketClientLiteException` passed. Under `eof-as-data` the 64-bit header ends in
  a protocol error at lengths 2 to 7, so the earlier test would have passed there.

The requirement map points at the new tests: `HEADER-001-TEST` adds the limit theory
and the truncation theory, and `HEADER-002-TEST` adds both header-only theories.

## Limits

- The failing-first runs used the `net10.0` library build only. The five-target
  matrix ran the tests against every library build.
- The changes in the copies are hand-written C# edits of generated code. They show
  that a test detects one missing or shifted check. They are not Dafny mutants and
  say nothing about the proof.
- Two rules are only covered together with another rule, so no parse-level test can
  show either alone: the bit-63 rule (with the size limit) and the opcode check for
  opcodes 3 to 7 (with the sequence rule).
- From S3 until `90c156f` the controlled capture job failed on this branch.
  `tools/Pdd/RetentionDefects.cs` required exactly 14 results from the
  `ParserRetentionTests` baseline, and the two new theories bring it to 40. Every case
  passed; only that count failed. On the owner's decision, `90c156f` requires 40 and
  names both theories in `RetentionCaseCounts`. The first checks are in
  [part-2/checks/](evidence/header-test-parity/part-2/checks/README.md); the rerun,
  in which every check passed, is in
  [part-2/checks-90c156f/](evidence/header-test-parity/part-2/checks-90c156f/README.md).
- The RSV and opcode rules have no requirement entry of their own. `HEADER-001`
  excludes them, and its test obligation names `RejectsMalformedFrames`, whose rows
  test them.
