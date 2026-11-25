# Caller-side Close defects and synchronous Dispose

Written 2026-11-25 from `releases/10.6.0` at `b7a6bcc` (the same tree as `main` at
`08679d0`). Tests, a repeatable experiment and evidence only: no production file, no
disposition and no gate changed. Commands, raw output and every attempt:
[`docs/evidence/close-caller-defects/`](../docs/evidence/close-caller-defects/README.md).

The Close decision is proved (`Protocol.CloseStep`) and its five catalogued mutants are
caught. The Stryker gate mutates `Service/WebsocketConnectionHandler.cs` only. The code
that calls the close step and acts on its answer is in `Service/WebsocketSenderHandler.cs`:
`AdvanceClose` and its callers `WriteFrameSerializedAsync`, `NotifyPeerClose`,
`NotifyReadFailure` and `WaitForPeerCloseAsync`. For reconnect, defects where the function
is right and the shell misuses its answer survived every test
([reconnect-decision.md](reconnect-decision.md), "Seeded defects, both ways"). Part A
repeats that method for Close. Part B closes the synchronous `Dispose` gap that the owner
review of the Stryker dispositions found
([stryker-close-pilot.md](stryker-close-pilot.md), "Owner review of the dispositions,
November 22, 2026").

## Part A: defects on the caller's side of the close step

### Method

17 defects, each one edit in `WebsocketSenderHandler.cs`, seeded one at a time in a
`git archive b7a6bcc` copy: the six kinds the task named (14 defects) and three found by
reading the code. The catalog is
[`catalog.json`](../docs/evidence/close-caller-defects/catalog.json). Each was run against
`ControlledCloseTests` and `CloseBoundaryTests` three times and against the whole test
project once (library net11.0, Release), after an unchanged copy passed. A run counts as a
catch only when a test fails on an assertion: a `TimeoutException`, a build failure or an
inconclusive run is not a catch. The four survivors and the three defects caught only by the
whole project were then run against the whole project three more times (each verdict held).

A verdict from three runs does not show that a catch is reliable, so each caught defect's
detecting test was then run 30 times with the defect seeded. That found one race:
`release-early` fails `PeerWaitEndsByControlledTimeoutOrReadFailure` in only 28 of 30 runs
(on its named assertion in 24), because its `AssertPending` races the disposal that the defect releases early. Another
existing test catches it in 30 of 30. Every "Caught by" below failed on its named assertion
in 30 of 30 runs.

### Results

| # | Defect | What was changed | Breaks | Controlled and boundary (3 runs) | Whole project (runs) | Caught by, 30 of 30 runs |
| ---: | --- | --- | --- | --- | --- | --- |
| 1 | `send-ignored` | The Close frame is written whatever the step answered | CLOSE-001 | caught | caught (1) | `ControlledCloseTests.PostWriteFailureRejectsQueuedAndLaterDataAndSuppressesClose` |
| 2 | `send-inverted` | Close written when the step said no, dropped when it said yes | CLOSE-001, CLOSE-004 | caught | caught (1) | `ControlledCloseTests.CancellationBetweenFragmentsAllowsOnlyClose` |
| 3 | `release-early` | The peer-close waiter is woken on every event | CLOSE-003, CLOSE-004 | caught | caught (1) | `ProtocolStateTests.CloseResponseTimeoutMakesTheTransportTerminal` (the PeerWait test: 28 of 30) |
| 4 | `release-never` | The waiter is never woken; only the peer timer ends the wait | CLOSE-003, CLOSE-005 | caught | caught (1) | `CloseBoundaryTests.ReadFailureReleasesReadinessBeforeObserverError` |
| 5 | `state-not-stored` | The new state is discarded | CLOSE-001, CLOSE-002 | caught | caught (1) | `ControlledCloseTests.QueuedPongIsSuppressedAfterPeerClose` |
| 6 | `state-stored-outside-lock` | `_stateLock` is released around the store of the new state | CLOSE-LOCK | **survived** | survived (4) | none |
| 7 | `peer-close-outside-lock` | `NotifyPeerClose` calls `AdvanceClose` without the lock | CLOSE-LOCK | **survived** | survived (4) | none |
| 8 | `send-decided-outside-lock` | `WriteFrameSerializedAsync` decides the Close send before it takes the lock | CLOSE-LOCK | **survived** | survived (4) | none |
| 9 | `read-failure-as-received` | `NotifyReadFailure` reports a received Close | CLOSE-003 | caught | caught (1) | `CloseBoundaryTests.ReadFailureReleasesReadinessBeforeObserverError` |
| 10 | `peer-close-as-failure` | `NotifyPeerClose` reports a timeout or failure | CLOSE-001 (RFC 6455 5.5.1: reply to a Close) | caught | caught (1) | `ControlledCloseTests.CancellationBetweenFragmentsAllowsOnlyClose` |
| 11 | `timeout-as-received` | The peer-wait timeout reports a received Close | none observable (below) | **survived** | survived (4) | none |
| 12 | `peer-close-skipped` | `NotifyPeerClose` does not call `AdvanceClose` | CLOSE-003, CLOSE-005 | caught | caught (1) | `ControlledCloseTests.ParserSignalsPeerCloseOrReadFailureBeforeTimerExpiry`, the peer Close row |
| 13 | `read-failure-skipped` | `NotifyReadFailure` does not call `AdvanceClose` | CLOSE-003, CLOSE-005 | caught | caught (1) | `CloseBoundaryTests.ReadFailureReleasesReadinessBeforeObserverError` |
| 14 | `timeout-skipped` | The timeout path does not call `AdvanceClose` | CLOSE-003 | survived | caught (4) | `ProtocolStateTests.CloseResponseTimeoutMakesTheTransportTerminal` |
| 15 | `write-failure-not-notified` | Extra: a failed frame write is not reported to the close state | CLOSE-FAILURE, CLOSE-001 | caught | caught (1) | `ControlledCloseTests.PostWriteFailureRejectsQueuedAndLaterDataAndSuppressesClose` |
| 16 | `pong-suppressed-after-own-close` | Extra: a queued Pong is dropped once our own Close is reserved | none written; the code comment allows Pong until the peer's Close | survived | caught (4) | `ProtocolStateTests.PongIsAllowedUntilPeerCloseThenSuppressed` |
| 17 | `data-after-close-allowed` | Extra: data frames are not rejected after the closing handshake started | RFC 6455 5.5.1 | survived | caught (4) | `ProtocolStateTests.ReceivedCloseBlocksDataAndSendsOnlyOneResponse` |

The three extra defects (15 to 17) misuse what the step returns or the state it leaves,
in the same lines as the call: 15 is the second caller of `NotifyReadFailure`, the write
path's catch; 16 and 17 read `_closeState` to decide on Pong and data frames.

For the owner to quote: **of 17 caller-side Close defects seeded in
`WebsocketSenderHandler.cs`, the existing tests caught 13, each with a test that failed on
its named assertion in 30 of 30 runs. The four survivors are three lock defects, which no
test can reach without a seam inside `AdvanceClose`, and one wrong event that, by reading,
changes no behaviour.** Unlike reconnect, the shell's misuse of the verified answer is
caught here; what is not caught is the lock.

No new test was written for Part A. Each surviving defect either needs a production seam
to be reached, or changes no behaviour:

- **The three lock defects (6, 7, 8)** break the boundary assumption CLOSE-LOCK ("All
  CloseStep calls serialize access to the same sender state under `_stateLock`"), whose
  exclusion reads "No proof of C# locking, state persistence or exhaustive interleavings".
  Each opens a window of a few instructions inside `AdvanceClose` or just before it. A
  wrong result needs a second close event, on another thread, between the read and the
  store. No callback, delegate or status runs inside that window, so a test cannot hold a
  thread there with a barrier. It could only hope for the race. Forcing it needs an
  internal hook inside `AdvanceClose`, which is a production change and out of scope here.
  Left for the owner.
- **`timeout-as-received` (11)** stores state 3 (sent and received) instead of 5 (sent and
  terminal). By reading, every later decision is the same. `WaitForPeerCloseAsync` runs
  only after `SendCloseHandshakeAsync` returned. It returns without the reservation bit
  only when the state already has the terminal bit, and then the waiter is already
  released and the timer cannot win. So the sent bit is always set when the timeout
  fires. From state 3 or 5, Send returns no send, Received and TimeoutOrFailure return
  release, and `Started` and `ReceivedOrTerminal` are both true. The model differs, and
  the behaviour does not. Recorded as equivalent, for the owner to confirm.

### The repeatable experiment

`./scripts/pdd.sh close-caller-defects --output <fresh-directory>` (`tools/Pdd/CloseCallerDefects.cs`)
runs the 13 caught defects. It copies the repository inputs, runs a baseline of the named
tests, then applies each edit alone. Each must fail its named test on its named assertion,
in the stated number of rows, with the other rows passing. A timeout message, a build
failure, a missing row or an extra failing row does not count (`tools/Pdd.Tests/CloseCallerEvidenceTests.cs`).
The first run failed on `release-early`, which is how its race was found
(`part-a/pdd-close-caller-defects-1/`). The second, at `e6fc58a`, passed 13 of 13 in 34
seconds (`part-a/pdd-close-caller-defects-2/`). The command is standalone: it is not part
of `capture --job controlled` or `aggregate`, so it changes no CI gate. Whether to wire it
in is the owner's decision. Nine of the 13 named assertions are xunit's own text
("Collections differ", "The collection contained 2 items" or "4 items", "No exception was
thrown"), because the detecting tests use bare `Assert.Equal`, `Assert.Single` and
`Assert.Throws`. That matching holds only as long as xunit's wording does.

### Extending the Stryker gate to `WebsocketSenderHandler.cs`

Measured, not changed. An exploratory run in a copy at `e6fc58a`, with only the
`mutate` pattern of `stryker-config.json` changed, ran on this host in 8 minutes 37 seconds
with the configured 8 workers. It found 166 testable mutants: 112 Killed, 53 Survived,
11 NoCoverage, 1 Timeout, 3 CompileError (score 63.84 %). For comparison, the gate's own run
(`WebsocketConnectionHandler.cs`, same host and workers, at `e6fc58a`) took 6 minutes
27 seconds. CI runs the Stryker job with 2 workers
(`--stryker-concurrency 2`, a 1800-second command budget). The same exploratory run with
2 workers took **30 minutes 4 seconds on this host**. That already exceeds the budget,
before CI's slower runners (about 2.4 times slower than the dev container for the Dafny mutants,
run 43041451305). Its outcomes also differed: 110 Killed and 3 Timeout, against 112 and 1
with 8 workers. Two mutants turned from Killed into Timeout, and a gate would need to
disposition both outcomes. Adding this file to the existing job would therefore break its
budget. It would need its own job, or more workers.

In the caller-side lines (`AdvanceClose`, its three callers, `WriteFrameSerializedAsync`)
Stryker produced 32 mutants: 25 Killed and 7 Survived. None of the 7 is one of the 17
defects above:

- `if (release) _closeReady.TrySetResult(false)` (line 45). The value is never read.
- `ConfigureAwait(true)` (lines 67 and 433). No synchronization context exists on these paths in the tests.
- Removal of `timer.CancelAsync()` and its `ConfigureAwait` (line 74). The peer timer keeps
  running after the peer's Close. That is a resource or liveness matter, which CLOSE-TIMER excludes.
- Two exception message strings (lines 428 and 430).

Stryker's operators cannot express the lock defects, the wrong events or a missing call
on a path with more than one statement. Those are the defects that matter here, and
`close-caller-defects` covers the ones that can be caught. Extending the gate would add,
at a minimum, 64 new dispositions (53 survivors and 11 NoCoverage, 44 of the survivors
outside the Close code) for the owner to review. Recommendation: **not worth its run time
as a gate now**. It would break the current budget, and it would add review work for
little more than the caller defects already caught. If wanted, keep it as an occasional
exploratory run.

## Part B: synchronous Dispose

The owner review records four survivors on the synchronous `Dispose` path of
`WebsocketConnectionHandler.cs` (source hash `fab51b41…`, unchanged by this work). The
mutants are identified by the dispositions' keys:

| Line | Pilot id | Mutant | Now |
| ---: | --- | --- | --- |
| 373 | 659 | `_tcpConnectionService?.Dispose();` removed | **Killed** by `SynchronousDisposeReleasesTransport`, both rows: 30 of 30 runs fail "Synchronous Dispose did not release the transport"; in the Stryker gate run at `e6fc58a`, Killed by it (both rows) |
| 353 | 650 | `closeOnce is not null && …` becomes `closeOnce is not null \|\| !closeOnce.IsValueCreated` | Killed. In the gate run at `e6fc58a`, Stryker reports it killed by `SetupAdmissionIsOwnedUntilTeardown` (it was already Killed in earlier local reports). `SynchronousDisposeReleasesTransport("no-close")` also kills it in an isolated copy, 30 of 30: "Synchronous Dispose threw: System.NullReferenceException" (the condition dereferences a null close task outside the `try`) |
| 361 | 656 | `!closeTask.Wait(3 s)` becomes `closeTask.Wait(3 s)` | Not tested, explained below |
| 362 | 657 | The block `_ = closeTask.ContinueWith(t => _ = t.Exception, …)` removed | Not tested, explained below |

`SynchronousDisposeReleasesTransport` (in `ControlledCloseTests`, two rows) asserts only what
the abort path must still do: `Dispose` returns (run off the test thread, under the
15-second diagnostic bound), does not throw, and releases the transport. Neither row runs
`Dispose` during an in-flight Close. `"no-close"` disposes a handler that never installed
a close task. `"close-finished"` disposes one whose shared close task has already
completed. So nothing here asserts graceful Close behaviour on this path, which
CLOSE-OBSERVER excludes. Its first version reported the 353 mutant as a raw
`NullReferenceException`, because the harness cleanup disposed again and replaced the
named failure. The test now runs that cleanup itself (`9d1e8d7`); the earlier runs are kept.

**361 and 362** affect only fault observation, after the three-second wait has timed out.
With 361, the continuation is attached when the close finished in time, which does nothing,
and is not attached when the wait timed out. With 362, it is never attached. Either way,
the only effect is that a close task which later faults goes unobserved.
`DisconnectWebsocket` catches every exception, so the task can fault only if the
`Disconnected` status callback throws. A test that kills either mutant needs all of these:

1. synchronous `Dispose` while a graceful Close is still in flight, which CLOSE-OBSERVER
   excludes ("Concurrent synchronous Dispose … may abort graceful closure");
2. a real three-second wait, because the bound is a literal with no seam (CLOSE-TIMER
   excludes wall-clock guarantees);
3. a throwing status callback (CLOSE-OBSERVER: "Blocking callbacks … may prevent completion");
4. observing `TaskScheduler.UnobservedTaskException` after a forced garbage collection. That
   event is process-wide, so it would see faults from other tests running in parallel.

Each of these is excluded or unreliable, so no test was written. Both stay out of scope, as
classified. A test would become reasonable only if the three-second bound got a seam and the
callback contract said what a throwing `Disconnected` callback must do.

The dispositions and `required_killed` are unchanged. The gate run at `e6fc58a` passed
`stryker-triage` against them. Mutants that the gate run now reports Killed where the
dispositions' `pilot_outcome` says otherwise: 687 (line 373, by the new test), 678 (line
353), 594 (line 95), 632 (line 214) and 584 (line 50). Of these, only 687 changed because
of this work. Whether to require 687 in `required_killed` is the owner's decision.

## Run times

| Test | Per case, 30 runs |
| --- | --- |
| `SynchronousDisposeReleasesTransport("no-close")` | 0.007 to 0.011 s |
| `SynchronousDisposeReleasesTransport("close-finished")` | 0.016 to 0.021 s |
| `./scripts/pdd.sh close-caller-defects` | 34 s for the baseline and 13 edits |

## Left for the owner

- Confirm `timeout-as-received` as equivalent, and decide whether the three lock defects
  justify an internal seam inside `AdvanceClose`, so that a schedule can force the race.
- Whether to wire `close-caller-defects` into `capture --job controlled` and `aggregate`.
- Whether to add mutant 687 (line 373) to `required_killed`.
- Whether to extend the Stryker gate to `WebsocketSenderHandler.cs` (recommended: no).
- `pong-suppressed-after-own-close` is caught, but the behaviour it breaks, Pong allowed
  after our Close until the peer's Close, is written down only in a code comment, not in a
  requirement.
- The race in `PeerWaitEndsByControlledTimeoutOrReadFailure` under `release-early`. That
  test passes on unchanged code in every recorded run, and only its detection of that
  defect varies.
