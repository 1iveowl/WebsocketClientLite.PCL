# Header length rule: replay validation

**This is a replay made on 2026-11-11, not a record from 2026-11-07.** It checks
whether the regression test for RFC 6455 section 5.2, "the minimal number of
bytes MUST be used to encode the length", fails against the parser as it was
before the proof work. The rule arrived in squash commit `c482b0c` (#100) together
with its contract, tests and CI gate. No record shows that test failing before the
fix.

The first replay did not fail. It is retained below, unchanged. The test was then
strengthened on this branch and replayed again.

## Original evidence

The search for any record from 7 November that shows the test failing before the
fix found none. Local reflog, unreachable objects (`git fsck`), stashes and local
branches hold nothing from that work: the pre-squash branch
`feat/dafny-masking-pilot` is one commit, `83a1b67`, with the same tree as
`c482b0c`. The CI runs for #100 (42374169180), for the push of `c482b0c`
(42374275518) and for the baseline `a779602` (42374153142) all pass. The baseline
run predates the test.

A local session log of the 7 November work shows the order. `DataframeParsing.cs`
was switched to the verified core at 12:17:40 UTC. `RejectsMalformedFrames` was
added at 12:20:45. Its only test failures that session were in an unrelated
handshake test. The test was never run against the old parser, and replay 1 below
shows it would not have failed. The log stays local.
[original-session-timeline.json](evidence/header-length/original-session-timeline.json)
is an extract made on 2026-11-11: timestamps, files changed and the result line of
every test and proof run, with the log's SHA-256 and size so the extract can be
checked against it.

## Baseline

`a779602` (merge of release 9.2.0). `c482b0c` has the single parent `a779602`, so
the two candidate baselines are the same commit. The current source is `6eaa31e`
(`releases/10.2.0.0`), whose `src/`, `verification/` and `scripts/` are identical
to `30cf44a` (`main`). Each run used a separate `git archive` copy; only
`PayloadParsingTests.cs` differs from its commit, plus a diagnostic file in the
diagnostic copies. [identity.json](evidence/header-length/identity.json) holds the
hashes of the files as built, and [commands.json](evidence/header-length/commands.json)
holds each command and its result.

## Replay 1: the test as written passes on the old parser

The reduction adds the two non-minimal rows of today's `RejectsMalformedFrames`,
verbatim, to the `a779602` test class
([replay-test.diff](evidence/header-length/replay-1/replay-test.diff)). The test
harness is identical in both versions.

| Source | Result |
| --- | --- |
| `a779602` with the two rows | **2 passed, 0 failed.** The expected failure did not occur. |
| `6eaa31e`, test unchanged | 8 passed |

The rows end after the header. `81 7E 00 7D` announces 125 payload bytes and
carries none. The old parser accepts the header, tries to read the payload, reaches
the end of the stream and throws `WebsocketClientLiteException` ("Websocket
connection aborted unexpectedly"). The test only required that exception type, so
it passed for the wrong reason. This run is a failed expectation and is kept as
such; it is not relabelled by replay 2. The `baseline.exit` file has no duration
because `bc` is not installed on this host; the TRX file holds the timings.

## Diagnostic: the rule is a real behaviour change

A separate copy of each source adds
[HeaderLengthDiagnostic.cs](evidence/header-length/diagnostic/HeaderLengthDiagnostic.cs),
which records outcomes and asserts nothing. It is not part of the replay.

| Frame | Payload sent | `a779602` | `6eaa31e` |
| --- | --- | --- | --- |
| 16-bit field carrying 125 | none | EOF: connection aborted | protocol error (header) |
| 16-bit field carrying 125 | 125 bytes | **accepted, Length=125** | protocol error (header) |
| 64-bit field carrying 65535 | none | EOF: connection aborted | protocol error (header) |
| 64-bit field carrying 65535 | 65535 bytes | **accepted, Length=65535** | protocol error (header) |

The old parser accepted non-minimal lengths. The current parser rejects them from
the header, before it reads any payload.

## Strengthening and replay 2

The rows of `RejectsMalformedFrames` are unchanged. Its body now also asserts that
the error is not the end-of-stream error, the pattern
`RejectsSequenceBeforeAttemptingPayloadRead` in the same file already uses
([strengthening.diff](evidence/header-length/replay-2/strengthening.diff)). For the
non-minimal rows this means the header itself has to be refused before any payload
read. Replay 2 runs all eight rows.

| Row | Rule | `a779602`, original body | `a779602`, strengthened | `6eaa31e`, strengthened |
| --- | --- | --- | --- | --- |
| `C1 00` | RSV1 set, 5.2 | fail | fail | pass |
| `83 00` | reserved opcode 3, 5.2 | fail | fail | pass |
| `81 7E 00 7D` | non-minimal 16-bit length, 5.2 | pass | **fail** | pass |
| `81 7F 00 00 00 00 00 00 FF FF` | non-minimal 64-bit length, 5.2 | pass | **fail** | pass |
| `81 7F 80 00 00 00 00 00 00 00` | 64-bit length high bit, 5.2 | pass | pass | pass |
| `88 01 00` | one-byte Close body, 5.5.1 | fail | fail | pass |
| `88 02 03 ED` | Close code 1005, 7.4.1 | fail | fail | pass |
| `88 03 03 E8 FF` | Close reason not UTF-8, 5.5.1 | fail | fail | pass |

Both non-minimal rows now fail on the old parser with
`Assert.DoesNotContain() Failure: Sub-string found ... "aborted unexpectedly"`
and pass on the current one.

## Limits

- The high-bit row passes on the old parser because that parser rejected the frame
  through its size limit: any length with bit 63 set exceeds `int.MaxValue`. No
  parse-level test can separate the RFC high-bit rule from the size policy. In
  `HeaderValid` the clause `(length & 0x8000000000000000) == 0` is implied by
  `length <= 0x7fffffff`.
- The strengthened assertion depends on the end-of-stream message text, as the
  existing `RejectsSequenceBeforeAttemptingPayloadRead` does.
- The replays ran on one host against the `net10.0` library build only. With the
  strengthened test on this branch, `./test-all-tfms.sh Release` passed 269 tests
  for each of the five library builds.
