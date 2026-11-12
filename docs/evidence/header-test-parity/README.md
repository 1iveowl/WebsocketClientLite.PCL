# Header test parity, part 1: evidence made on 2026-11-11

Step S3 of `docs/pdd-10.3-session-plan.md`, brief Appendix C, W1 and W2 item 2.
Everything here was made on 2026-11-11 on one host: Linux x86-64 Debian dev
container, kernel 7.2.11+deb13-amd64, .NET SDK 10.0.403, test host net10.0, Release.

## Sources

| Commit | What it is |
| --- | --- |
| `fe0bd48` | `origin/releases/10.3.0.0` at the start of S3, after #136 (`d238c9e`) and #139. The tests before S3. |
| `58bc568` | This branch: the S3 test changes. Only `PayloadParsingTests.cs` and `ParserRetentionTests.cs` differ from `fe0bd48`. The production code is unchanged. |
| `a779602` | Merge of release 9.2.0, the parser before the proof work, as in `docs/header-length-validation.md`. |

Each run uses its own copy, made with `git archive <commit> | tar -x` in a scratch
directory and never in the working checkout. In each copy, at most one guarded check
is removed by [tools/defect.py](tools/defect.py). [tools/run.sh](tools/run.sh) makes
the copy, applies the change, writes `<run>.diff` against the unchanged commit and
`<run>.identity.txt` with the SHA-256 of the files as built, and then runs:

```text
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release
  --filter "FullyQualifiedName~PayloadParsingTests.Parse_FrameExceedingMaxFrameSize_Throws|
            FullyQualifiedName~PayloadParsingTests.Parse_OversizedControlFrame_Throws|
            FullyQualifiedName~PayloadParsingTests.RejectsMalformedFrames|
            FullyQualifiedName~ParserRetentionTests.InvalidHeadersRejectBeforePayloadRead"
  --results-directory <evidence> --logger "trx;LogFileName=<run>.trx" --logger "console;verbosity=normal"
```

Every run is kept with its `.log`, `.trx` and `.exit` file. None was repeated.

The removed checks are in the generated C# of the verified core,
`verification/generated/Masking.g.cs`, where production runs them. No copy is
verified with Dafny. These runs test the tests and are not Dafny mutants.

| Change | What is removed |
| --- | --- |
| `size-limit` | The configured frame size limit: `length <= limit` in `HeaderValid`, and in `Step` the limit check for a first frame. The check of a continuation against the remaining message budget stays. |
| `size-bound` | `size-limit`, and also the `length <= 0x7fffffff` bound in `HeaderValid`. |
| `control-length` | The control frame length rule (125 bytes or less): the marker check in `PrefixValid`, the length check in `HeaderValid` and the length check in `Step`. The control FIN rule stays. |
| `high-bit` | The bit-63 clause of `HeaderValid`. |
| `reads-past-header` | In `DataframeParsing.ReadDataframeAsync`, the parser tries to read one byte past the header before it throws the same protocol error. Every decision is unchanged. |
| `replay` | `a779602` with [ParserRetentionTests.a779602-replay.cs](tools/ParserRetentionTests.a779602-replay.cs) added: the new theory as it is, with the counting stream and `MissingPayload` copied in, because `a779602` has neither. |

## W1: tests that can pass on end of stream (confirmed by running)

These copies of `fe0bd48` run the tests as they were before S3. Each copy ran 10
cases.

| Test | Check removed | Result | W1 weakness |
| --- | --- | --- | --- |
| `Parse_FrameExceedingMaxFrameSize_Throws` | `size-limit` | run and passed, 10 of 10 | **Confirmed.** Without the size check, the header is accepted and the parser ends on EOF with the same exception type |
| `Parse_OversizedControlFrame_Throws` | `control-length` | run and passed, 10 of 10 | **Confirmed**, for the same reason |
| `RejectsMalformedFrames`, high-bit row | `high-bit` | run and passed, 10 of 10 | **Confirmed.** The size limit still rejects the row |
| `RejectsMalformedFrames`, high-bit row | `size-bound` | run and passed, 10 of 10 | **Confirmed.** The bit-63 rule still rejects the row |

Logs: `w1-confirm/`.

## Failing first: the changed and new tests

These copies of `58bc568` run the S3 tests. Each copy of `58bc568` ran 19 cases.

| Run | Result | Cases that failed |
| --- | --- | --- |
| `size-limit` | run and failed, 17 of 19 | `Parse_FrameExceedingMaxFrameSize_Throws`, and the theory row `82 7E 00 C8` with limit 100: `Assert.Throws() Failure: No exception was thrown` |
| `size-bound` | run and failed, 17 of 19 | The same two. The high-bit row of `RejectsMalformedFrames` still passes, through the bit-63 rule |
| `control-length` | run and failed, 17 of 19 | `Parse_OversizedControlFrame_Throws`, and the theory row `89 7E 00 C8`: `No exception was thrown` |
| `reads-past-header` | run and failed, 10 of 19 | All 9 theory rows: bytes read or read attempts are one above the expected count, for example `Invalid header read 5 bytes in 5 attempts, expected 4`, and for the unpadded bit-63 row `read 10 bytes in 11 attempts, expected 10`. Every other test passes, so only the theory detects the read order |
| `high-bit` | run and passed, 19 of 19 | None. This is the expected result: the high-bit row stays combined coverage (comment on the row), and no test claims the bit-63 rule on its own |
| `replay` (`a779602`) | run and failed, 5 of 11 | 6 of the 9 theory rows. The old parser accepted RSV1, opcode 3, a masked server frame and both non-minimal lengths (`No exception was thrown`). It read the extended length of the oversized control frame before it rejected the frame (`read 4 bytes in 4 attempts, expected 2`). It rejected the fragmented control row, the limit row and the high-bit row from the header, so those rows pass there, and `reads-past-header` shows them failing. The other two cases are the `a779602` versions of the two `Parse_*` facts |
| `current` (`58bc568`, unchanged) | run and passed, 19 of 19 | None |

Every theory row fails in at least one copy, and each changed `Parse_*` test fails
in the copy that lacks its check. Logs: `failing-first/` and `current/`.

## Five-target matrix

`./test-all-tfms.sh Release` ran on the working checkout at `58bc568`, with only
this evidence folder untracked: run and passed, 278 of 278 tests against each of
the netstandard2.0, netstandard2.1, net8.0, net9.0 and net10.0 library builds
(269 before S3, plus the 9 theory rows), in 132 seconds. The output is in
`current/test-all-tfms.log` and `current/test-all-tfms.exit`.

On the same tree, `./scripts/pdd.sh validate` ran and passed with 16 requirements
(`current/pdd-validate.log`). The Pdd tests,
`dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release`, ran and passed, 262 of 262
(`current/pdd-tests.log`). The coverage gate and the capture jobs were not run; they
belong to S4.

## Limits

- The high-bit row of `RejectsMalformedFrames` cannot show that the bit-63 rule alone
  is enforced, because any length with bit 63 set also exceeds `int.MaxValue`. The
  `high-bit` and `size-bound` runs show that each rule alone rejects the row.
- In the theory, the rows that have length 0 in `PayloadParsingTests` (RSV1,
  opcode 3, fragmented control) announce one payload byte, so that a payload read
  shows. Without it, reading the payload of a zero-length frame reads nothing.
- The expected counts follow `DecodeHeader`'s contract: status 0, "more header
  bytes needed", requires `PrefixValid`. So a header that fails on its first two
  bytes is refused after 2 bytes, even with a 126 marker.
- The copies ran against the net10.0 library build only.
