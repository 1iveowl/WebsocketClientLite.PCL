# P3-A validation and review record

P3-A only. The [ownership record](../verification/queued-write-ownership.md)
separates safety from conditional progress, documents the reproduced transport
abort defect and scoped correction, and lists the finite schedules and exclusions.
The specification used was `docs/pdd-phase-3-plan.md` at planning commit `78966de`.
Implementation is based on `releases/10.2.0.0` at `bed679e`; the planning branch was
read without changing its worktree or importing later increments.

## Accepted G machine baseline

Downloaded post-merge push [run 42408136157](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42408136157),
attempt 1, artifact 13588159091. Its gate passes; all 158 source hashes match
`bed679ea8bd3e90ee5ce986d04c36c2a78ae9c56`. All four job identities match; the
11 human-review obligations remain not-run. G's 40 Killed / 29 Survived /
7 NoCoverage / 0 Timeout / 3 CompileError / 26 Ignored outcomes are retained,
including all 16 required Killed fingerprints. Earlier F failure is not relabeled.

[Retained G receipt archive](evidence/p3-a/g-baseline.zip), SHA-256
`6701d1d713c8f4b9ec85981fbeaefd0eeddc4cbb348a642071ce628a83f47065`.
Its receipt.json digest is
`1d7b19e709f7a2dcc8cfb4e50baa6aacd40c8c4fe42772ff8bb914f770fc4756`.

## Reproduction and intermediate attempts

The original transport source from `bed679e`, plus the reduced test now present
in fix commit `b4462c7`, fails: owner entered → noncancelable waiter queued →
transport disposed → owner settled → waiter did not settle within 15 seconds.
Bounded cleanup records the same failure after another 15 seconds. The TRX, log
and exact reproduction source files are retained. This is a controlled progress
failure, not an overlap assertion or a Stryker Killed result.

The scoped fix in `b4462c7` retains the async semaphore and rejects admission
following abort. The reduced test then passes (17 ms TRX case duration). The first
complete schedule run passes all 13 cases. The initial isolated capacity-two
experiment passes its baseline and detects the named overlap assertion. Those
intermediate runs are not substituted for final source-matched checks.

The first tooling run passes 160 and fails two stale manifest-count expectations:
one assumed 11 requirements, the other classified every non-Close requirement as
payload. Updating the count to 12 and selecting REASSEMBLY/MASK explicitly preserves
all six payload obligations. After adding queued evidence rejection fixtures, all
173 tooling cases pass. No test failure was dismissed as timing noise or retried
unchanged into a success claim.

[Diagnostic archive](evidence/p3-a/diagnostics.zip), SHA-256
`38e01e9fcc34d8ea1d1c6cd834ce83c2e8f8f451b58d4015a9d6b7f8877d919f`.

## Final local machine evidence

Checked commit: `6dfa910759ac8272dd52f65abf7667389c75c13b`, clean worktree.
The protocol, tests, controlled and manual probe records have identical relevant
source hashes before and after. Subsequent documentation/archive commits do not
relabel this checked commit; PR CI captures its own final source revision.

Host: Linux x86-64 Debian container, kernel 7.2.11+deb13-amd64, AMD Ryzen 9 9955HX,
32 logical CPUs, SDK 10.0.403, actual test host .NET 10.0.14. Five library builds
are not five runtime versions. Existing package caches were warm; isolated defect
copies were freshly built. Protocol work overlapped the probe/matrix and controlled
copies; no other session was controlled. Contention is not measured. Peak RSS,
billing and runner-minute deltas were not measured; no pricing claim is made.

| Check | Result | Measured seconds |
| --- | --- | ---: |
| 50-process queued smoke probe | 50/50 pass, 15 cases each (13 new + inherited fragment + runtime) | 68.29 |
| Release solution build | pass, zero warnings/errors | 0.94 |
| Five-target Release matrix | 233 tests per target, all pass | 134.44 |
| Coverage run and unchanged 70% gate | 82.2% lines, 76.3% branches | 26.99 |
| Tooling fixtures | 173 pass | 9.36 |
| Pinned proof, trust audit, generated-source comparison | pass | 11.22 |
| Dafny baseline plus all 15 mutations | baseline passes, all detected | 144.43 |
| Inherited ordering defect | named assertion detected after baseline | 4.30 |
| All 13 inherited G boundary experiments | named assertions detected | 22.72 |
| New frame-overlap defect | named assertion detected after baseline | 4.07 |

The new cases' summed TRX durations across the five targets total 0.2234 seconds;
the longest single new case is 0.0194 seconds. These are case timings, not a
controlled before/after estimate of incremental matrix wall time. The 134.44-second
matrix and G's 149.49-second CI baseline ran in different environments and cannot
establish a performance improvement. The new 4.07-second isolated command fits
the provisional 120-second incremental allowance; no existing command/job limit
was raised. The 50-repeat probe remains manual, not an added per-PR cost.

The existing CI Stryker job remains required, unchanged, with two workers, mixed
mutants disabled and all 16 critical fingerprints required Killed. Local evidence
does not claim a fresh full Stryker run or a complete four-job aggregate; those
are provided by final PR CI. Human review remains separate from machine results.

[Complete local capture](evidence/p3-a/local-validation.zip), SHA-256
`977d0a1c79683ce4e763e26c65a13f4d7dfde73918ec7bb78454168ff1aa0222`.
It retains raw TRX, command/execution records, source hashes, per-case durations,
actual runtime probes, coverage and proof/mutation results. The new isolated
experiment rejects wrong source/edit/runtime, missing cases, baseline failure,
unrelated assertions, compile failure, process timeout and cancellation. A stuck
owner is a diagnostic failure, not an accepted overlap detection.

## Final scope review and inherited CI timeout

The initial complete local capture at `bfb779e` passed and is retained as
[initial-local-validation.zip](evidence/p3-a/initial-local-validation.zip)
(SHA-256 `ad398cecd936c12dda606bf05c6325cf6a06d4be5001baf2fae3fdf2783c7b8a`).
Final review changed the successful post-cancellation probe to Ping when the two
data slots were already used, keeping each scenario within two data sends and
one control contender. A second review corrected pre-cancelled trace labels so
they no longer imply gate ownership or queueing after rejected admission.

Only this session's second validation processes were explicitly interrupted to
make that trace correction. The probe records failure at repetition 37 (zero-based,
38 command records); protocol mutation work records interruption. Those are not
passing evidence. The fresh final capture above reran all 50 repetitions, the
Release five-target/coverage checks, all controlled experiments and the entire
protocol suite on clean source.

The first PR [CI run 42427106321](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42427106321)
(at source head `b39ff04`, synthetic merge `34c6f61`) failed the inherited protocol
mutation gate. `MASK-002-tail-xor-to-or` emitted its expected loop-invariant failure,
but `HeaderRoundTrip` also timed out at its unchanged 60-second obligation limit;
the command ended in 65.533 seconds. The gate correctly classified it **inconclusive**
and left the following four mutants not-run. No protocol or proof source changed
in P3-A. This failure is retained, not relabeled by later runs.

A bounded local diagnostic ran the existing payload suite with
`DOTNET_PROCESSOR_COUNT=2`, the same pinned tools, two verifier cores and unchanged
60/90-second obligation/process limits. Its baseline and all ten payload mutations
passed; the implicated mutant was detected in 8.659 seconds. Together with the two
complete local captures this does not reproduce the CI timeout. It does not prove
that runner load caused it, nor establish that the inherited proof timing is
stable. No proof, gate or timeout was changed to obtain a pass. Final PR CI must
independently pass all existing gates before merge; continued proof instability
must block acceptance and be handled separately from P3-A ownership semantics.

Retained additional records:

- [interrupted-validation.zip](evidence/p3-a/interrupted-validation.zip), SHA-256 `496d41ec2b7b509708da6d2f28bd971fdbd83c998d713c12a9bcebc37e8a3d14`.
- [proof-diagnostic.zip](evidence/p3-a/proof-diagnostic.zip), SHA-256 `ec7f59c58162fd3b27991a3b3c99c7edd9f98019c491643cc5211db0d726b3e4`.
- [first-ci-protocol.zip](evidence/p3-a/first-ci-protocol.zip), SHA-256 `f820a4aa9fc78ae86a1a35acce2b14c56c909331b544e832f28e0fe06d2f03ae`.

## Review and follow-up limits

Implementation review retains the semaphore without requesting its native wait
handle, preserves cancellation exception shapes, and leaves in-flight I/O progress
external. It adds no FIFO promise or atomic connect/dispose contract. Source review
confirms no changes to sender/connection-handler logic, generated protocol,
GitVersion.yml, G dispositions, workflow budgets or another session's checkout.
The narrowly scoped correction is a separate commit; all new evidence is tested
C# ownership, not Dafny proof of concurrency.

Semantic acceptance must be recorded against the reviewed PR source; a machine
receipt cannot supply it. The owner explicitly authorized merge after completion.
No publication or P3-B work is included. Review recurring cost over subsequent CI
runs; this single local sample is not the plan's future ten-run median/maximum
series. Revisit if the gate begins using AvailableWaitHandle, delegate ownership
changes, or added costs exceed the provisional allowances. No further tool or
broader schedule exploration is justified by this increment.
