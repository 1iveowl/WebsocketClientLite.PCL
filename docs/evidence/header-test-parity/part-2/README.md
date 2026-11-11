# Header test parity, part 2: evidence made on 2026-11-11

Step S4 of `docs/pdd-10.3-session-plan.md`, brief Appendix C, W2 items 4, 6 and 7.
Everything here was made on 2026-11-11 on one host: Linux x86-64 Debian dev
container, kernel 7.2.11+deb13-amd64, .NET SDK 10.0.403, test host net10.0, Release.
The validation note is [docs/header-test-parity-validation.md](../../../header-test-parity-validation.md).

## Sources

| Commit | What it is |
| --- | --- |
| `bcdcda6` | This branch: the S4 test changes. Only `PayloadParsingTests.cs` and `ParserRetentionTests.cs` differ from `4c449d2`, the end of S3. The production code is unchanged. |
| `a779602` | Merge of release 9.2.0, the parser before the proof work, as in part 1. |

Each run uses its own `git archive <commit> | tar -x` copy in a scratch directory,
never the working checkout. [run-part-2.sh](../tools/run-part-2.sh) makes the copy,
applies at most one change, writes `<run>.diff` against the unchanged commit and
`<run>.identity.txt` with the SHA-256 of the files as built, and then runs:

```text
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release
  --filter "FullyQualifiedName~.RejectsMalformedFrames|FullyQualifiedName~.TruncatedHeadersNeverProduceFrames|
            FullyQualifiedName~.Parse_FrameExceedingMaxFrameSize_Throws|FullyQualifiedName~.RejectsMessageAboveLimit|
            FullyQualifiedName~.InvalidHeadersRejectBeforePayloadRead|
            FullyQualifiedName~.ConfiguredLimitAcceptsLimitAndRejectsSuccessorFromHeader|
            FullyQualifiedName~.AccumulatedLimitRejectsBeforePayloadRead"
  --results-directory <evidence> --logger "trx;LogFileName=<run>.trx" --logger "console;verbosity=normal"
```

The filter adds the existing limit tests (`Parse_FrameExceedingMaxFrameSize_Throws`,
`RejectsMessageAboveLimit`, `AccumulatedLimitRejectsBeforePayloadRead`), so each run
also shows whether a test that existed before S4 detects the same change. Every run
is kept with its `.log`, `.trx` and `.exit` file. None was repeated.
[summary.txt](summary.txt) lists every failing case of every run; it was made with
[summarize-part-2.py](../tools/summarize-part-2.py).

The changes are in [defect-part-2.py](../tools/defect-part-2.py), apart from
`size-limit` and `reads-past-header`, which reuse part 1's [defect.py](../tools/defect.py)
unchanged. Protocol checks are removed in `verification/generated/Masking.g.cs`, where
production runs them. No copy is verified with Dafny; these runs test the tests and are
not Dafny mutants.

| Change | What it does |
| --- | --- |
| `rsv2-rsv3` | `PrefixValid` checks only RSV1 (mask `0x40` instead of `0x70`). |
| `unknown-opcodes` | `PrefixValid` and `Step` treat every opcode except 3 as known. Opcode 3 stays refused, so the existing row keeps passing. |
| `unknown-opcodes-sequence` | `unknown-opcodes`, and `Step`'s idle rule also admits data opcodes 4 to 7. |
| `size-limit` | Part 1: the configured limit in `HeaderValid`, and `Step`'s limit check for a first frame. |
| `limit-plus-one` | The configured limit is one byte too large for a single frame: `HeaderValid` allows `limit + 1`, and so does `Step` for a first frame. A continuation keeps its budget. |
| `limit-strict` | `HeaderValid` compares with `<` instead of `<=`: a frame of exactly the limit is refused. |
| `reads-past-header` | Part 1: the parser tries to read one byte past the header before it throws. |
| `eof-as-cancel` | End of stream in `TryFillAsync` returns "no data", as cancellation does, instead of throwing. The parser then returns null. |
| `eof-as-data` | End of stream in `TryFillAsync` ends the read as if it were complete. The buffer keeps the bytes it held before. |
| `replay` | `a779602` with [ParserRetentionTests.a779602-replay-part-2.cs](../tools/ParserRetentionTests.a779602-replay-part-2.cs): the four S4 tests with their rows and statements verbatim, and the helpers they need copied in. |

## Results

| Run | Result | Cases that failed |
| --- | --- | --- |
| `current` (`bcdcda6`, unchanged) | run and passed, 51 of 51 | None |
| `rsv2-rsv3` | run and failed, 47 of 51 | The RSV2 and RSV3 rows of `RejectsMalformedFrames` and of the theory: `No exception was thrown`. The RSV1 rows pass |
| `unknown-opcodes` | run and failed, 41 of 51 | The opcode 11 to 15 rows of both tests: `No exception was thrown`. The rows for opcodes 3 to 7 pass: `Step`'s idle rule still refuses them |
| `unknown-opcodes-sequence` | run and failed, 33 of 51 | The opcode 4 to 7 and 11 to 15 rows of both tests. The opcode 3 row passes |
| `size-limit` | run and failed, 43 of 51 | All 6 rows of the new limit theory, the theory's limit row and `Parse_FrameExceedingMaxFrameSize_Throws` |
| `limit-plus-one` | run and failed, 45 of 51 | All 6 rows of the new limit theory: `No exception was thrown` for the frame one above the limit. **Every test that existed before S4 passes**, including `Parse_FrameExceedingMaxFrameSize_Throws`, the theory's limit row and both accumulated limit tests |
| `limit-strict` | run and failed, 43 of 51 | All 6 rows of the new limit theory (`Frame of exactly the limit 1 was refused`), and both rows of the existing `AccumulatedLimitRejectsBeforePayloadRead`, which already sends a first fragment of exactly the limit 1 or 4 |
| `reads-past-header` | run and failed, 23 of 51 | All 20 theory rows, including the 11 new reserved-value rows, all 6 rows of the limit theory (`read 3 bytes in 3 attempts, expected 2`, and 5 for 4, 11 for 10), and both rows of `AccumulatedLimitRejectsBeforePayloadRead` |
| `eof-as-cancel` | run and failed, 49 of 51 | Both truncation rows, at every length: `0: null; 1: null; 2: null; 3: null` for the 16-bit header, and 0 to 9 for the 64-bit header |
| `eof-as-data` | run and failed, 49 of 51 | Both truncation rows. 16-bit header: length 0 a protocol error, 1 to 3 a frame. 64-bit header: 0 and 2 to 7 a protocol error, 1, 8 and 9 a frame |
| `replay` (`a779602`) | run and failed, 13 of 48 | All 13 reserved-value rows of `RejectsMalformedFrames` and of the theory, the non-minimal, masked and Close rows as in part 1 and `docs/header-length-validation.md`, and the theory's control-length row (`read 4 bytes in 4 attempts, expected 2`). The limit theory, both truncation rows, the high-bit rows, the fragmented control row, the theory's limit row and `a779602`'s own `Parse_FrameExceedingMaxFrameSize_Throws` pass: `a779602` already had those behaviours |

The `a779602` copy ran 48 cases: the 47 replayed cases and `a779602`'s own
`Parse_FrameExceedingMaxFrameSize_Throws`, which the filter also matches.

## Notes on the runs

- The run script's first `a779602.diff` was malformed: it archived `src` and
  `verification` from `a779602`, which has no `verification`, so the archive was
  empty and the diff listed every file as new. The test run was not affected. The
  diff was made again with `git archive a779602 src` and
  `diff -ruN -x bin -x obj -x TestResults`, and the file holds that diff: one file,
  `ParserRetentionTests.cs`, added.
- `.exit` holds the duration of the `dotnet test` command alone, with a warm build
  server. The copy and archive steps are not included.
- Under `eof-as-data` the 64-bit header ends in a protocol error at lengths 2 to 7:
  the parser decided on bytes that never arrived. The test before S4 accepted any
  `WebsocketClientLiteException`, so it would have passed at those lengths. The
  theory now requires the end-of-stream error at every length.

## Checks on the full tree

The checks of S4, item 4, are in [checks/](checks/) and in the validation note.
