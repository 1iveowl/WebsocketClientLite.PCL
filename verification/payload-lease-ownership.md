# P3-B: sender frame leases

Based on release `e810f72` containing P3-A PR #114, whose final reviewed delivery
and owner-authorized merge are recorded in that PR. The specification is P3-B in
`docs/pdd-phase-3-plan.md` at planning commit `78966de` (still on its planning branch).
G's accepted post-merge baseline remains run 42408136157, artifact 13588159091;
see the retained receipt and exact hashes in `docs/p3-a-validation.md`.

The sender rents before frame admission and owns storage until the awaited send
settles. A waiting message has no lease yet. A cancelled frame waiter returns its
lease without invoking the writer. A suppressed queued Pong likewise returns
without writer use. A write failure, including after a prefix is transferred,
releases its lease only after the delegate Task settles. Invalid UTF-16 is rejected
by strict byte counting before rent, including validation of all text-list entries.
The production five-second Close observation timeout ends observation only: the
original delegate deliberately stays pending, and its lease remains held until
explicit settlement in the harness. Cleanup releases and drains controlled work;
fifteen-second waits are diagnostics, not public deadlines.

`PayloadLeaseTests` uses the existing write delegate plus an internal optional
`ArrayPool<byte>` constructor argument defaulting to Shared. A delegate alone
cannot reliably observe Shared's Return or reuse. The deterministic pool allocates
19 extra sentinel bytes, records identities/counts/clear requests and poisons only
when ownership is returned. Every harness read checks ownership first; last reads
occur before completing the write Task. Success, failure, cancellation, suppression
and timeout check one Return per successful Rent. Wire expectations independently
encode headers and scalar XOR against the generated random key; sizes 0, 125, 126,
65535 and 65536 cross header boundaries. F's fixed masking vectors and invalid
Decode adapter cases remain required and unchanged.

Binary arrays are borrowed through completion of SendBinary, including every array
in a binary list. Snapshotting the enumerable captures references, not bytes.
Repeated references to stable input arrays are supported and tested; the caller's
bytes are unchanged. Concurrent caller mutation remains unsupported. No copy policy,
public ownership API, queue memory limit or new dependency is introduced. Invalid
pool behavior (such as renting caller-owned storage) is not a supported aliasing
case. Return requests clearing; the ArrayPool contract only promises clearing if
the pool retains the array, not universal zeroization.

Safety assumes pool contract compliance and that a stream delegate stops accessing
the array when its returned Task settles. Progress additionally assumes runnable
continuations, returning callbacks/cleanup and eventual external settlement. These
are tested C# boundaries, not proofs of the allocator, runtime, Rx, I/O, arbitrary
schedules or wire delivery. The production change adds one pool reference and uses
virtual Rent/Return on that reference; allocation/performance impact is unbenchmarked.

`lease-defects` executes a clean baseline then three independent exact edits in
temporary copies: Return after starting but before awaiting the writer, omitted
Return, and capacity in place of initialized count. Each must fail its named
lease/count assertion, with exit 1 and matching runtime/TRX/execution/source records.
Timeout, compilation, missing evidence and unrelated failures cannot count as
assertion detection. Controlled capture and aggregate require these experiments;
`LEASE-001-TEST` also requires every theory row on five library targets. No G/A gate
or budget changes. `lease-probe` repeats all 14 cases plus runtime in 50 processes,
including the real five-second timeout; it is manual, not a per-PR repeat cost.

Human semantic acceptance belongs to revision-specific PR review. Revisit on pool,
write completion or copying-policy changes and when recurring CI exceeds the plan's
provisional cost allowances. No later Phase 3 increment is included.
