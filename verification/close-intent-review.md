# Close intent review — increment A

This is a source-based intent review and a set of decisions for PR review, not
owner approval or a verification receipt. It adds no runtime/API guarantee.
The implementation baseline is `81859273eab73940dc8453df9f076816573f1d03`;
the phase 2 plan was established in `f60dd9b` on `releases/10.2.0.0`.
Normal PR review records acceptance against the reviewed revision. There is no
`approved` flag in the manifest.

## Decisions

The [Close manifest](requirements.json) describes five deliberately separate
obligations. The [phase 2 plan](../docs/pdd-phase-2-plan.md) remains the delivery
plan. Existing tests are identified as obligations, not as observed passing runs.

### CLOSE-001

**Alternatives:** “send exactly one Close frame” or “reserve at most one send
attempt, and return a send decision for an eligible request.” Choose the latter.
`ProtocolCore.Protocol.CloseStep` accepts states 0..7 and events 0..2. For event 0,
`send` is true exactly when the reservation and terminal bits are clear; successful
reservation sets bit 1. Repeated transitions retain that bit. This combines a
positive obligation with duplicate suppression, so rejecting every request is
insufficient. The at-most-once sequence argument depends on state persistence and
serialization, not just a single invocation's postconditions.

`WebsocketSenderHandler.WriteFrameSerializedAsync` reserves inside the connection's
frame gate, under `_stateLock`, before awaiting `_writeFunc`. It can fail after
reservation. [RFC 6455 §5.5.1](https://www.rfc-editor.org/rfc/rfc6455.html#section-5.5.1)
requires a Close response when one has not already been sent; the reservation
model is a library mechanism, not a proof of response delivery. Both duplicate-send
and suppressed-eligible-send mutations remain obligations for increment B.

### CLOSE-002

**Alternatives:** monotonic wire progress or preservation of modeled state bits.
Choose bit preservation: `next & state == state` for every permitted transition.
`AdvanceClose` must store the returned state under the lock. This covers the
reservation, receive and terminal bits, including combinations absent from the
selected compiled test examples. It does not prove that I/O progresses.
Increment B must target bit loss; the existing compiled test is partial coverage.

### CLOSE-003

**Alternatives:** permission to immediately dispose the transport or readiness of
the modeled handshake wait. Choose readiness. `release` is equivalent to terminal
bit set or both reservation and receive bits set in `next`. `AdvanceClose` uses it
to complete `_closeReady`; it does not dispose the transport. With a peer-initiated
Close, readiness can occur before the response write finishes.

[RFC 6455 §7.1.2](https://www.rfc-editor.org/rfc/rfc6455.html#section-7.1.2)
describes closing after both frames have been exchanged. Reservation is weaker
than exchange; CLOSE-004 carries the separate ordering responsibility. The exact
release equivalence requires checking both premature and suppressed release in B.

### CLOSE-004

**Alternatives:** all disposal paths await a completed Close write or only the
graceful path does, under explicit assumptions. Choose the latter.
`DisconnectWebsocket` awaits `SendCloseHandshakeAsync` through an Rx five-second
timeout before waiting for the peer. `DisposeAsync` awaits the shared lazy close
task before releasing parser/TCP resources. `WriteFrameSerializedAsync` awaits the
write delegate inside `TcpConnectionService.WriteSerializedAsync`.

Send failure and send timeout are best-effort abort exits; the catch proceeds to
`Disconnected`. Timeout does not establish cancellation/completion of the original
write. Synchronous `Dispose` can wait only three seconds, or skip waiting when
closure has already started. Concurrent unsubscribe, synchronous disposal or
external transport disposal can therefore interrupt a pending write and are
excluded from this graceful-path requirement. No new public promise is inferred
from `DisposeAsync_AwaitsCloseHandshake_AndCompletesSubscriber`: it observes a
loopback frame, but does not pause an in-flight write. That controlled ordering
obligation is explicitly deferred to E.

[RFC 6455 §7.1.1](https://www.rfc-editor.org/rfc/rfc6455.html#section-7.1.1)
also discusses waiting for server TCP closure. This manifest does not establish
that behavior or full closing-handshake conformance. Failure treatment is informed
by [§7.1.7](https://www.rfc-editor.org/rfc/rfc6455.html#section-7.1.7), while the
particular time limits are library policy.

### CLOSE-005

**Alternatives:** disposal always completes within five seconds or completion is
conditional on scheduling, stream and callback behavior. Choose conditional
completion. There are two separate five-second waits: send observation and then
peer response. They are not a single disposal deadline. A received Close satisfies
the wait only once a reservation is recorded (unless already terminal).
`WaitForPeerCloseAsync` uses `Task.Delay` and records event 2 on expiry;
read/write failures also record event 2 through `NotifyReadFailure`.

The Rx send timeout follows a different catch path and need not record that state
transition. Do not equate every timeout with a proved `CloseStep` terminal event.
Callbacks, resource disposal and continuations must return/run. Existing tests
cover delayed peer response through disposal and timeout at sender level separately;
they do not prove end-to-end disposal under controlled timeout/failure schedules.
Those tests belong to E. No unconditional termination or exhaustive concurrency
claim follows from this map.

## Boundary register (increment C)

Increment A introduced inline boundary seeds. Increment C moves those stable IDs
and assumption wording into [boundaries.json](boundaries.json), with code owners,
dependent requirements, test obligations and exclusions. The anchors below remain
the review discussion for each entry. The generated `trusted-boundaries.md` audit
format remains separate and unchanged.

<a id="boundary-lock"></a>
### CLOSE-LOCK

Owner: `WebsocketSenderHandler.AdvanceClose` and its callers
`WriteFrameSerializedAsync`, `NotifyPeerClose`, `NotifyReadFailure`, and
`WaitForPeerCloseAsync`. State starts at zero, stays in range and is updated under
`_stateLock`. C# lock ownership is reviewed/tested, not proved by Dafny.

<a id="boundary-write"></a>
### CLOSE-WRITE

Owner: `WebsocketSenderHandler.WriteFrameSerializedAsync`,
`TcpConnectionService.WriteSerializedAsync`, and
`WebsocketConnectionHandler.DisconnectWebsocket`. The frame gate serializes writes;
the injected delegate reports actual write completion/failure. A reservation or
readiness notification cannot substitute for awaiting write completion. A completed
stream write still does not prove receipt by the peer.

<a id="boundary-failure"></a>
### CLOSE-FAILURE

Owner: `WebsocketSenderHandler.NotifyReadFailure` and the write catch path;
`WebsocketConnectionHandler.DisconnectWebsocket` handles send failure/timeout.
Partial writes and transport failures permit abort, without retrying a Close on an
unknown wire prefix. Graceful delivery is excluded on those paths.

<a id="boundary-timer"></a>
### CLOSE-TIMER

Owner: `WebsocketSenderHandler.WaitForPeerCloseAsync` and
`WebsocketConnectionHandler.DisconnectWebsocket`. Task/Rx timers and continuations
must eventually execute. Starvation, timer delivery and wall-clock bounds are
outside the sequential proof. A send timeout can stop waiting without stopping I/O.

<a id="boundary-observer"></a>
### CLOSE-OBSERVER

Owner: `WebsocketConnectionHandler` shared close task, `DisconnectWebsocket`,
`DisposeAsync`, and `Dispose`, plus outer service/client teardown. Callbacks and
resource cleanup must return. A concurrent synchronous disposal/unsubscribe is an
abort path, excluded from graceful write ordering. Controlled tests in E must
state whether such interference is present, rather than silently assuming it away.

## Evidence gaps and acceptance

CLOSE-001..003 require the existing Dafny verification command, compiled test
identities, intent review and future B mutations. CLOSE-004..005 have **no Dafny
contract**: existing tests and caller review are separate obligations, with missing
controlled schedules explicitly assigned to E. An empty contract list is valid
for tested-only requirements and never implies proof coverage.

This review checks wording against callers and RFC text. It does not freeze intent
on behalf of the owner. PR reviewers should accept or revise the scoped decisions,
especially reservation versus delivery, abort exceptions and conditional liveness.
Subsequent semantic changes must update this review and the manifest together.

## Increment F: reassembly and masking intent decisions

These additions are proposed intent decisions for revision-specific PR review. They do not record approval. The established review-file path is retained for compatibility.

### REASSEMBLY-001

Given stable retained segments and their exact valid total, production Merge returns a fresh exact-sized concatenation in segment order, assuming the native-copy contract.

Chosen: production concatenation conditional on the native-copy contract. Rejected alternative: describing the native adapter as fully proved scalar code.

The production proof assumes NativeCopy.CopySlice; it is not the fully scalar proof. C# retained-total bookkeeping and the entire asynchronous parser are not proved by this requirement. No proof of generated C#, compiler/runtime correctness, successful allocation, concurrent buffer access or complete RFC conformance.

### REASSEMBLY-002

Under the same stable-segment and exact-total preconditions, MergeScalar returns a fresh exact concatenation using verified element copies, without the native-copy assumption.

Chosen: a separate scalar-reference guarantee. Rejected alternative: treating selected equality tests as proof of compiled implementation equivalence.

Comparing production and scalar outputs on selected cases is not a proof of their compiled C# equivalence. No proof of generated C#, compiler/runtime correctness, successful allocation, concurrent buffer access or complete RFC conformance.

### REASSEMBLY-003

For distinct source/destination arrays and an in-bounds offset, the native copy adapter copies exactly the source bytes while preserving the source and destination outside that slice.

Chosen: tested-only native assumption, preserving independent source/guard tests. Rejected alternative: counting verification of an extern declaration as implementation proof. No proof-mutation obligation is assigned to this external runtime primitive.

This is a tested assumption, not a proved algorithm; the external Dafny declaration is not proof evidence. No Dafny mutation obligation: mutating the extern contract would change an assumption rather than test its implementation. Independent native-copy tests remain mandatory. No proof of generated C#, compiler/runtime correctness, successful allocation, concurrent buffer access or complete RFC conformance.

### MASK-001

For a distinct key with at least four bytes and count equal to payload length, Apply XORs each payload byte with the corresponding repeating key byte and preserves the key through its data-only frame condition.

Chosen: exact XOR and frame/preservation guarantees under the mapped preconditions, with independent expected-byte tests. Rejected alternative: round-trip tests alone, which can miss paired defects or a no-op.

The cryptographic randomness of mask-key generation is outside this XOR/preservation requirement. No proof of generated C#, compiler/runtime correctness, successful allocation, concurrent buffer access or complete RFC conformance.

### MASK-002

CopyMasked writes XOR-masked bytes from exactly the requested source prefix into the destination slice, preserving the source and every destination byte outside that slice.

Chosen: exact XOR and frame/preservation guarantees under the mapped preconditions, with independent expected-byte tests. Rejected alternative: round-trip tests alone, which can miss paired defects or a no-op.

No proof of array-pool ownership, UTF-8 encoding or completed wire writes. No proof of generated C#, compiler/runtime correctness, successful allocation, concurrent buffer access or complete RFC conformance.

### MASK-003

MaskSlice XORs exactly the requested in-place payload slice using the four earlier key bytes and preserves every byte outside the slice, including the key and guards.

Chosen: exact XOR and frame/preservation guarantees under the mapped preconditions, with independent expected-byte tests. Rejected alternative: round-trip tests alone, which can miss paired defects or a no-op.

No proof of encoding, header construction, array-pool ownership or completed wire writes. No proof of generated C#, compiler/runtime correctness, successful allocation, concurrent buffer access or complete RFC conformance.

<a id="boundary-payload-ownership"></a>
### PAYLOAD-OWNERSHIP

Input arrays and retained segments stay stable during each operation; destination ownership is exclusive and required source/destination non-aliasing holds.

No concurrent caller mutation or proof of C# ownership; aliased retained segments are allowed when stable, but destination aliasing is not.

<a id="boundary-reassembly-totals"></a>
### REASSEMBLY-TOTALS

Callers provide the exact retained-byte total within Size bounds; empty payload segments do not change that sum.

The parser total and retained segment list are C# bookkeeping, not verified by the Merge proof.
P3-C adds [production adapter evidence](parser-retention.md) for finite retained
sequences, byte limits and sequential handshake/header scratch consumption. It
keeps the single-reader and stable-input assumptions; machine tests do not
constitute human acceptance of those boundaries.

<a id="boundary-native-copy"></a>
### NATIVE-COPY

System.Buffer.BlockCopy implements the external CopySlice contract: exact slice copy and preservation outside it, with distinct arrays and valid bounds.

This is the existing explicit external assumption. MergeScalar does not depend on it. No native implementation proof or native mutation claim is made.

<a id="boundary-mask-adapter"></a>
### MASK-ADAPTER

Adapters establish null/alias/key-length and slice bounds, retain buffer ownership, and pass the intended payload count and header key to generated masking methods.

No proof of the C# adapter, pool lifetime, entropy, encoding or I/O completion.

## Increment G: lifecycle intent review (acceptance pending)

CLOSE-004/CLOSE-005 now require the G tests and independent defect experiments
listed in [the evidence record](close-evidence-g.md). The existing async pipeline
body is exposed as an internal task-returning method so cancellation tests can
observe its finally completion. Its caller retains parser/control projection and
Rx subscription semantics. A default-preserving internal scheduler injection
lets periodic-ping disposal be tested through actual virtual ticks.

Noncancelled source completion awaits the fallback close before observer
completion. Cancelled subscription teardown leaves close ownership to disposal
and does not explicitly complete the observer. Source error records read failure,
then emits Aborted and the original observer error before fallback close when the
subscription remains active. Rx can suppress a second terminal; tests distinguish
the internal explicit callback contract from outer automatic completion.

Both disposal methods stop periodic pings before closure; this does not promise
that cancellation forcibly stops arbitrary I/O. DisposeAsync must finish without
pumping its caller's synchronization context once the injected write and peer
conditions have completed. Other context/callback progress remains assumed.
No intended runtime change, expanded native trust or unconditional liveness claim
is approved by these tests. Human semantic acceptance is still revision-specific
PR review, separate from passing machine gates.

### WRITE-001

P3-A adds tested-only single-connection ownership evidence. Message-wait cancellation
has no frame reservation; frame-wait cancellation may own composed storage but has
not reserved a protocol transition. An in-flight delegate owns frame serialization
until it settles. A terminal notification rejects subsequent data admission;
already admitted I/O may settle after notification or abort. See
[the detailed ownership and review record](queued-write-ownership.md).

<a id="boundary-write-progress"></a>
**WRITE-PROGRESS:** finite callbacks/cleanup and eventual scheduling remain trusted.
An in-flight stream operation must settle or be explicitly released in a test.
After abort, the semaphore remains alive for queued tasks to acquire and reject;
its managed state is reclaimed by GC. No AvailableWaitHandle is requested. No FIFO
or unconditional completion is asserted. Human semantic review remains not-run.

### LEASE-001

Review the finite schedules and trusted contracts in [payload lease ownership](payload-lease-ownership.md).
The internal pool defaults to Shared; no public copying or caller-mutation policy
changes. Safety requires the lease through delegate completion, exactly one return
with clearing requested, and initialized count. Observation timeout does not end
write ownership. Pool compliance, relinquishment on Task completion, stable caller
inputs and external progress remain assumptions. Machine receipts cannot record
human acceptance; review the PR source revision separately.
