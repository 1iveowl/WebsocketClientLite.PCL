# Header test parity, review fixes: evidence made on 2026-11-11

After S4, a fresh session ran `/code-review high` on PR #140 and posted 8 findings
(inline comments r4543690634 to r4543691472). Under the owner decision for the
overnight chain and the controller decision on #140 (both 2026-11-11), the findings are
fixed on this branch before merge point M2. Everything here was made on 2026-11-11 on
one host: Linux x86-64 Debian dev container, kernel 7.2.11+deb13-amd64, .NET SDK
10.0.403, test host net10.0, Release. The validation note is
[docs/header-test-parity-validation.md](../../../header-test-parity-validation.md).

## The findings and what was done

| # | Finding | Done |
| --- | --- | --- |
| 1 | The `0x83` row is combined coverage like 4 to 7, but the comment and PR decision 1 said 4 to 7 | The comment sits above the `0x83` row and says 3 to 7. The new run `known-removed` shows it. The validation note already said 3 to 7; it now names the run |
| 2 | `RetentionDefects.cs` pins the baseline count at 40 | The count is `RetentionCaseCounts.Values.Sum() + 1` (the `+ 1` is `RecordHostRuntime`) |
| 3 | The PR description says the aggregate requires both theories through `RetentionCaseCounts`, but `Aggregation.cs` checks a count only when the test is present | `Aggregation.cs` is not changed (no decision authorizes it). The PR description now says what each check does, and lists the presence gap as a follow-up |
| 4 | `TruncatedHeadersNeverProduceFrames` left the loop on any other exception | A last `catch (Exception other)` lists the prefix and goes on. Runs `eof-as-other` and `eof-as-other-c82da89` |
| 5 | The masked row announced length 0, against the theory comment | The row is `81 81 01 02 03 04 00`: mask key and one payload byte. Run `reads-past-header` |
| 6 | The header-only theory copied the rows of `RejectsMalformedFrames` by hand | One list, `PayloadParsingTests.InvalidHeaders`, with the header bytes each decision needs; both tests read it through `MemberData` |
| 7 | `Parse` never disposed its fake connection and socket | `using var fake` |
| 8 | `Header(limit)` and `Header(limit + 1)` were built twice | Built once each, and the expected sizes are their lengths |

The test code is commit `cdcc233`. No production code, public behaviour or Dafny
contract changed. The test names and their case counts are unchanged:
`RejectsMalformedFrames` 19, `InvalidHeadersRejectBeforePayloadRead` 20.

## Failing-first runs (`failing-first/`)

The runs reuse part 2's method. [run-review.sh](../tools/run-review.sh) is
[run-part-2.sh](../tools/run-part-2.sh) with the paths of this worktree. It makes its
own `git archive` copy, applies at most one change, keeps `<run>.diff` and
`<run>.identity.txt`, and runs the same filter (the S4 tests and the existing limit
tests, 51 cases). The new changes are in [defect-review.py](../tools/defect-review.py);
the others reuse [defect.py](../tools/defect.py) and
[defect-part-2.py](../tools/defect-part-2.py) unchanged. The batch is
[batch.sh](failing-first/batch.sh); it ran once, under the shared timing lock, and its
output is [batch.log](failing-first/batch.log), including the load and the other
`dotnet` processes when it started (S6 was running a test). No run was repeated.
[summary.txt](failing-first/summary.txt) lists every failing case, made with
[summarize-part-2.py](../tools/summarize-part-2.py).

| Change | What it does |
| --- | --- |
| `known-removed` | `PrefixValid` and `Step` do not check `Known` for any opcode. Unlike part 2's `unknown-opcodes`, opcode 3 is not kept refused |
| `known-sequence-removed` | `known-removed`, and `Step`'s idle rule also admits data opcodes 3 to 7 |
| `eof-as-other` | End of stream during a read throws `InvalidOperationException` instead of `WebsocketClientLiteException` |

| Run | Commit | Result |
| --- | --- | --- |
| `current` | `cdcc233`, unchanged | run and passed, 51 of 51 |
| `reads-past-header` | `cdcc233` | run and failed, 28 cases: all 20 rows of the header-only theory, the masked row included (3 bytes read, 2 expected), all 6 successor rows, and both `AccumulatedLimitRejectsBeforePayloadRead` rows |
| `rsv2-rsv3` | `cdcc233` | run and failed, 4 cases: the RSV2 and RSV3 rows of both tests |
| `unknown-opcodes-sequence` | `cdcc233` | run and failed, 18 cases: the rows for opcodes 4 to 7 and 11 to 15 of both tests |
| `known-removed` | `cdcc233` | run and failed, 10 cases: the rows for opcodes 11 to 15 of both tests. **The rows for opcodes 3 to 7 pass in both tests**: the `0x83` row is combined coverage, as the rows for 4 to 7 are |
| `known-sequence-removed` | `cdcc233` | run and failed, 20 cases: the rows for opcodes 3 to 7 and 11 to 15 of both tests |
| `limit-plus-one` | `cdcc233` | run and failed, 6 cases: all 6 rows of the limit theory |
| `eof-as-other` | `cdcc233` | run and failed, 2 cases: both truncation rows, each listing every prefix (`0: InvalidOperationException: end of stream (eof-as-other); 1: ...`) |
| `eof-as-other-c82da89` | `c82da89`, before the fix | run and failed, 2 cases: both truncation rows fail on the escaping `InvalidOperationException`, with no prefix listed |

The shared rows still fail first in both tests after the refactor, and the
`known-removed` run is the kept run the corrected statement about the `0x83` row
needed. The `a779602` replay was not repeated: its replay file holds the S4 tests
verbatim, and the rows did not change.

## Correction to an S4 statement

[part-2/checks-90c156f/README.md](../part-2/checks-90c156f/README.md) says that host
qualification and the aggregate now require both theories with their counts. That
holds for host qualification (`HostEvidence.cs`, which requires every
`RetentionCaseCounts` test to be present with its count in each host cell) and for the
retention baseline that the aggregate validates again (`ValidateRetentionReport`,
net10.0). It does not hold for the aggregate's per-target test matrix:
`Aggregation.cs`, lines 83 to 86, marks a test failed only when it is present with
another count. There, a missing theory is caught by the test obligations instead:
`HEADER-002-TEST` names both theories and `HEADER-001-TEST` the limit theory, and a
referenced test missing from any target's results leaves its obligation `not-run`,
which fails the gate. The S4 file is kept as it was.
