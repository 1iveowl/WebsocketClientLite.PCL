# P3-A: queued admission, cancellation and abort

Scope: one connection, at most two data sends and one control contender. The
specification is P3-A in `docs/pdd-phase-3-plan.md` at planning commit
`78966de` (`origin/docs/pdd-phase-3-plan`); implementation starts from release
`bed679ea8bd3e90ee5ce986d04c36c2a78ae9c56`. The planning document was not yet on
that release branch. P3-B and later increments are excluded.

## Ownership and intent

| Phase | Owner and reservation | Cancellation/terminal outcome |
| --- | --- | --- |
| Waiting-message | Data has not acquired `_messageGate`; no frame reservation or composed frame for a single send. A list snapshots references before waiting, not payload bytes. | Cancellation propagates directly. Terminal state is checked after eventual frame admission. |
| Waiting-frame | Data holds message ownership; controls bypass it. Composition can own rented storage while waiting for the transport `_writeGate`. No SendStep/CloseStep reservation yet. | Cancellation is wrapped by the existing SendError exception contract. The frame delegate is not called. Terminal state is checked under `_stateLock` after admission. |
| In-flight | Transport owns frame serialization; data also owns message serialization. Protocol reservation already happened. The write delegate may still access its buffer. | Delegate completion/failure releases frame ownership. Abort can dispose the stream while this delegate remains active. It does not cancel an arbitrary delegate or certify delivery. |

`SendList` retains message ownership across its fragments. Explicit fragment
calls only own their individual call; manually composed messages still need one
application owner. The existing `CancellationBetweenFragmentsAllowsOnlyClose`
test covers cancellation after the first successful fragment, without duplication.

Safety: at most one frame delegate executes; a cancelled waiter does not write;
queued data does not reach the delegate after peer Close/read failure; a waiter
admitted after transport abort is rejected before calling its delegate. An I/O
operation already admitted before a concurrent abort may be interrupted or may
settle successfully in the controlled fake; neither outcome claims peer delivery.
The abort check is not a new atomic disconnect-versus-admission contract.

Progress is conditional on the current owner settling, runnable continuations
being scheduled, finite callbacks/cleanup returning, and either eventual admission
or cancellation waking the queued wait. No FIFO or starvation-freedom assumption
is made. A waiter with no cancellation token can drain after the owner releases;
it need not finish while that owner is held. The 15-second per-wait bound is a
diagnostic limit, not a public deadline. Cleanup releases held work, observes
started tasks and has its own 15-second drain bound. Arbitrary blocked delegates,
OS progress, weak-memory correctness and exhaustive schedules remain outside scope.

## Reproduction and scoped correction

The release implementation disposed `_writeGate` while a delegate owned it and a
non-cancelable `WaitAsync` was pending. The reduced production-method schedule was:
owner entered → waiter queued → Dispose → owner released and settled → waiter
still pending. `AbortDrainsNonCancelableFrameWaiterAfterOwnerSettles` failed with
`Stuck owner` at the 15-second bound and again during bounded cleanup (30 seconds
total). This is a reproduced controlled progress failure, not a claim about all
historical CI schedules or a Stryker Killed outcome.

The separate fix commit retains the async-only semaphore, just like the sender's
message gate. An interlocked disposed marker precedes stream disposal; after
acquisition, a volatile read rejects queued calls with ObjectDisposedException.
The owner always releases the live gate in finally. No native wait handle is
requested, so GC reclaims the managed semaphore state when no task owns it.
There is no public API, new cancellation policy, scheduler or lifecycle redesign.
The same reduced test passes after the fix. Connect-versus-dispose policy remains
outside A; a stream operation that never settles can still prevent queued progress.

## Selected schedules and detectors

`QueuedWriteTests` uses the existing transport and sender delegates; reflection
only installs an established MemoryStream, as in the E/G tests. No production
seam is added. Entry/release signals use asynchronous continuations. Calls execute
synchronously to their first pending gate await before the test records queueing;
there is no sleep or timer establishing event order.

| Test | Cases | Assertion |
| --- | --- | --- |
| CancellationBeforeOrWhileQueuedPreservesAdmission | 4 | Before message admission or queued at either gate, cancellation writes nothing; another send succeeds after release. |
| PreCancelledControlNeverAcquiresFrameOwnership | 2 | Pre-cancelled frame request with free/held frame gate never enters delegate; later data succeeds. |
| FrameSerializationAdmitsTwoDataSendsAndOneControlWithoutOverlap | 1 | Held data, message waiter and frame control contender drain with two data and one Ping writes, maximum one active delegate, either post-release order accepted. |
| TerminalNotificationRejectsDataWaitingAtEitherGate | 4 | Peer Close/read failure precedes admission at either gate; queued data rejects with no second delegate call. |
| AbortDrainsNonCancelableFrameWaiterAfterOwnerSettles | 1 | Abort while owner held and uncancelable transport waiter queued; release owner; waiter rejects without entering delegate. |
| AbortAllowsQueuedCancellationWhileOwnerRemainsInFlight | 1 | Abort and cancellation settle frame waiter while owner remains held; explicitly releasing owner drains it. |

`queued-probe --output <fresh-directory>` runs all 13 new cases, the inherited
between-fragments case and the actual runtime probe in 50 separate test processes.
It is manual smoke evidence, not a failure-probability bound. Ordinary matrix runs
include all cases on five library targets, using one .NET 10 host.

`queued-defect --output <fresh-directory>` copies repository inputs, first requires
a passing serialization baseline, then changes frame gate capacity from one to
two in the isolated copy. Exactly the named overlap assertion must fail; the
runtime probe must pass. The original source is never mutated. This experiment
is added to the existing controlled capture and aggregate, with before/after
source identity, exact edit hashes, CI identity and raw command/TRX records.
Missing theory rows, stale source, wrong runtime/edit, unrelated assertion,
compilation failure, interruption and process timeout cannot satisfy WRITE-001.
A `Stuck owner` diagnostic is a controlled missing-progress observation, never an
overlap assertion detection. The chosen negative experiment tests overlap only;
no omitted-release mutant or systematic schedule enumeration is claimed.

## Review decisions and limits

Implementation review decision: retain the gate rather than disposing it under
waiters; preserve the established cancellation exception shapes and E/G Close
ordering. Keep the single narrow frame-capacity negative experiment. No broad
Stryker expansion, new dependency, model checker or public ownership API is needed.
Keep all G gates, including 16 required Killed fingerprints, unchanged. Sender,
connection handler, generated protocol and GitVersion source are unchanged.

Human semantic acceptance is separate and remains not-run in machine receipts.
The PR must identify the reviewed source revision. The owner's later instruction
authorizes merge after completion; this document does not invent a reviewer
approval. Future review triggers: a production path requests AvailableWaitHandle,
a delegate lifecycle changes, or measured incremental matrix/controlled costs
exceed the plan's provisional 60/120-second allowances. Broader buffer leases and
connection-generation admission belong to B/D, not this change.

See [validation and retained evidence](../docs/p3-a-validation.md) for all attempts,
measured costs, host identity and source revisions.
