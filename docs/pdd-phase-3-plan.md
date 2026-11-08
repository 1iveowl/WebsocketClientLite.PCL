# PDD phase 3 plan

Extend evidence at the C# lifecycle and buffer boundaries that remain outside the
verified protocol core. Start with queued-write ownership during cancellation and
teardown. Keep Dafny, the existing controlled harness, independent byte tests and
source-bound receipts. Introduce another tool only if a bounded experiment shows
an assurance benefit that these seams cannot provide cheaply.

Status: implementation proposal for human review, reconciled with merged Phase 2 G.
This planning PR changes documentation only. It adds no dependencies, runtime behavior, tests,
verification infrastructure or new assurance claims. Target: `releases/10.2.0.0`.

## Planning baseline

Phase 2 G, [PR #112](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/112),
merged into `releases/10.2.0.0` as
`bed679ea8bd3e90ee5ce986d04c36c2a78ae9c56` on November 7, 2026 at 19:50:19 UTC.
The planning worktree fetched that revision; its tree matches inspected G head
`4625a04bbfbebfeeec751bfdad736f8e31c5b5eb`.

The planning baseline is the **successful post-merge push**
[CI run 42408136157](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42408136157),
attempt 1, including `evidence-gate`. It is not the green PR run. Downloaded
`pdd-receipt` artifact **13588159091** has archive SHA-256
`6701d1d713c8f4b9ec85981fbeaefd0eeddc4cbb348a642071ce628a83f47065`;
its `receipt.json` SHA-256 is
`1d7b19e709f7a2dcc8cfb4e50baa6aacd40c8c4fe42772ff8bb914f770fc4756`.
The receipt records the merge SHA, push event, run/attempt, clean source and matching
before/after identities for all four jobs. All 158 recorded source hashes match
the fetched files. Artifact retention can expire; the run, artifact ID and digests
identify the inspected evidence rather than promising permanent availability.

Observed baseline: Release build; 220 tests for each of five library targets, all
on .NET 10.0.14; 82.15% line coverage against the 70% gate; pinned proof, trust audit
and generated-code checks; baseline plus all 15 detected Dafny mutants; 162 tooling
cases; original ordering defect and all 13 G boundary experiments. Stryker 5.0.0
reports 40 Killed, 29 Survived, 7 NoCoverage, 0 Timeout, 3 CompileError and 26 Ignored
in the selected connection-handler scope. All 16 required fingerprints are Killed.
Machine evidence passes; human-review obligations remain separately `not-run`.
This is the accepted machine baseline for planning, not a promise of future
repeatability or human acceptance of every requirement.

Initial independent inspection used Phase 2 F at
`f3d5ed4d336c2c7f1df88643fb100db76acb0cc4`,
[PR #111](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/111).
Its [post-merge run 42401715327](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42401715327)
failed the evidence gate on a critical NotifyReadFailure mutation Timeout despite
passing tests/proofs and a completed Stryker command. That failed receipt remains
historical evidence; G's later success does not relabel its outcome.

### G reconciliation

G's [design record](../verification/close-evidence-g.md) and
[validation record](increment-g-validation.md) report no production runtime defect.
They reproduce mixed-mutant budget exhaustion and worker contention, but do not
reconstruct the exact historical CI schedule. Keep the original Timeout
inconclusive. G disables mixed mutants, uses two workers, drains stress-test
cleanup and adds a direct readiness assertion. It keeps the 30-second additional
timeout, 30-minute command and 40-minute job limits.

The following are already delivered by G and excluded from new Phase 3 work:
parser completion fallback/cancellation tests, explicit observer error/completion routing, stopping
periodic pings during either disposal mode, cancellation of a paused periodic
Ping, and non-pumping caller-context coverage of DisposeAsync. G supplies these
through `ObserveFrames` and an internal `IScheduler` defaulting to `Scheduler.Default`.
It retains real loopback coverage, adds 13 isolated assertion-based experiments
(including five independent read-failure repetitions), and requires 16 critical
Stryker fingerprints, including all seven inherited obligations. Phase 3 reuses
these seams, tests and receipt checks rather than creating replacements.

G's historical disposition entries labeled accepted-gap are not all still-open
work: those now required Killed cannot waive that critical gate. In the actual
post-merge report, six accepted-gap survivors remain at other continuation-context
sites: control projection, Ping/Pong, ObserveFrames waits and DisconnectWebsocket.
The incoming Close status-notification deletion and the outer completion-condition
variants were **Killed** in this run; their older gap/Timeout dispositions are
historical, not current undetected cases. Do not duplicate them as Phase 3 tasks.
Readiness and status callbacks are still distinct properties, and one successful
mutation run does not establish all callback behavior. Prioritize queued-write
and buffer ownership over a blanket custom-context campaign; revisit the latter
only for a concrete supported calling pattern or reproducible failure. P3-D may
exercise terminal routing across generations without reopening G's single-
connection acceptance tests.

Read together with the [original adoption plan](dafny-pdd-plan.md),
[Phase 2 plan](pdd-phase-2-plan.md), [verification guide](../verification/README.md),
[requirement map](../verification/requirements.json),
[boundary register](../verification/boundaries.json),
[intent decisions](../verification/close-intent-review.md),
[trusted extern audit](../verification/trusted-boundaries.md),
[controlled schedules](../verification/controlled-close.md),
[Stryker findings](../verification/stryker-close-pilot.md) and
[dispositions](../verification/stryker-close-dispositions.json).

## Goals and non-goals

Goals:

- Establish explicit ownership and terminal outcomes for selected queued work,
  cancellation, teardown and reconnect schedules, using production paths.
- Test the caller obligations behind `PAYLOAD-OWNERSHIP`, `REASSEMBLY-TOTALS` and
  `MASK-ADAPTER` without relabeling these C# boundaries as Dafny proofs.
- Separate actual runtime/OS execution from library-target compilation and testing.
- Keep negative experiments reproducible, diagnostic and bounded; retain source,
  environment and review identity as the evidence surface grows.
- Measure recurring CI and maintenance cost before broadening a gate.

Non-goals: full RFC conformance; proving Rx, .NET, sockets, TLS, native copy or the
Dafny compiler; unconditional termination; every possible schedule or memory
ordering; arbitrary caller mutation safety; a new public scheduler, buffer owner
or reconnect API; replacing the current protocol implementation; performance
redesign; automatic pruning of tests; another general PDD framework. No production
or test infrastructure implementation belongs in this planning PR. Leave
`GitVersion.yml` and `core.12592` untouched; publication remains the owner's step.

## What the current implementation establishes

Paths below refer to the production library under `src/main/WebsocketClientLite/`.

- [WebsocketSenderHandler](../src/main/WebsocketClientLite/Service/WebsocketSenderHandler.cs)
  holds `_messageGate` across `SendList`; controls bypass that gate. The transport's
  frame gate serializes `WriteFrameSerializedAsync`, where `_stateLock` protects
  `SendStep`/`CloseStep` and persists their results. Reservation occurs before the
  write delegate completes. `SendList.ToArray()` snapshots references, not payload
  bytes. Explicit opcode/fragment calls hold message ownership only for that call;
  a manually assembled message still needs one application owner across calls.
  Cancellation before admission differs from failure after a frame prefix
  may have reached the stream.
- [TcpConnectionService](../src/main/WebsocketClientLite/Service/TcpConnectionService.cs)
  awaits `_writeGate.WaitAsync(ct)` and disposes that semaphore in `Dispose`.
  Catching `ObjectDisposedException` on release is not evidence that every queued
  waiter completes. This is a high-priority investigation, not a reproduced defect.
  Microsoft documents that [semaphore disposal must not overlap other members](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.dispose?view=net-10.0);
  A must establish the real overlap before classifying the library outcome.
  `_headerScratch` assumes exactly one reader, after handshake completion.
- [WebsocketConnectionHandler](../src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs)
  shares a `Lazy<Task>` close, handles control frames through asynchronous Rx
  composition, and owns periodic Ping. `DisconnectWebsocket` observes send and peer
  waits separately. A timeout ending observation is not cancellation of the write.
  Synchronous disposal and unsubscribe are abort paths; they do not inherit
  `CLOSE-004` graceful ordering. G exposes `ObserveFrames` and injects the default
  periodic scheduler; reuse those seams. Other custom-context sites remain explicitly outside its coverage.
- [ClientWebSocketRx](../src/main/WebsocketClientLite/ClientWebSocketRx.cs) registers
  connections in `_activeConnections`, assigns one public `Sender`, reports
  connectivity through a shared subject, and removes services in `Finally` and
  disposal. Sequential `Retry`/`Repeat` creates new services; late events from an
  old connection and connect-versus-dispose admission need explicit scope.
  Caller-supplied `TcpClient` reuse is already excluded for reconnect.
- Sender `ComposeFrameAndSendCoreAsync` rents a frame buffer, masks into it, awaits
  sending, then returns it with clearing in `finally`. It can rent before waiting
  for the frame gate. A queued control frame therefore owns storage while waiting.
  [DataframeParsing](../src/main/WebsocketClientLite/Helper/DataframeParsing.cs)
  validates headers and accumulated length before allocating payload storage.
- [WebsocketParserHandler](../src/main/WebsocketClientLite/Service/WebsocketParserHandler.cs)
  keeps `state`, `total`, `first` and `segments` per subscription. Nonempty payloads
  are retained; controls update state/size without entering the segment list.
  Zero/one-segment fast paths bypass `Merge`; one-segment output reuses that array.
  `Merge`'s fresh-output guarantee is not a guarantee for every parser output.
  The parser must preserve exact totals and original message metadata itself.
- [NativeCopy.CopySlice](../src/main/WebsocketClientLite/Helper/VerifiedNativeCopy.cs)
  delegates to `Buffer.BlockCopy`. F already distinguishes production `Merge`,
  proved scalar `MergeScalar`, and tested-only native copy. Keep that distinction,
  its guard/source tests and the independent masking vectors.

## Risk and property map

IDs here are planning identifiers, not additions to the requirement manifest.
S = safety (a prohibited event never occurs); L = conditional progress.

| Priority / property | Boundary and failure concern | Existing evidence / remaining gap | Classification and proposed assurance |
| --- | --- | --- | --- |
| P0 / P3-WRITE (S,L) | `_messageGate` → `_writeGate` → `_stateLock`; cancellation or transport disposal while work is queued | E covers cancellation between fragments and post-write failure, not all admission/abort orders | Missing evidence; A tests ownership, release and settled outcomes. Do not infer FIFO fairness. |
| P0 / P3-LEASE (S) | Rent → compose → queued/in-flight write → Return | F proves bytes under preconditions, not asynchronous buffer lifetime | Missing evidence; B observes leases, exact initialized count and once-only return on each terminal path. |
| P1 / P3-RETAIN (S) | `Admit`/`Step` inputs ↔ retained list, total, first metadata and scratch reads | F tests Merge and selected integration cases; parser bookkeeping remains trusted | Missing evidence; C tests independent event sequences and boundary values, with local assertions if useful. |
| P1 / P3-EPOCH (S,L) | Connection registration, old `Finally`, shared `Sender` and IsConnected, reconnect | README reconnect guidance and error/teardown tests do not establish two-generation ownership | Missing evidence and policy choice; D reviews supported subscription/disposal semantics before asserting them. |
| P2 / P3-CONTROL (S,L) | Ping producer, queued Pong, parser completion/error, reentrant callbacks | E tests queued Pong; G covers completion/cancellation, observer routing, periodic cleanup and the disposal calling context | G evidence is inherited from the passed post-merge gate. Only new cross-gate/generation interactions belong to A/D/F; defer unrelated context variants. |
| P1 / P3-HOST (evidence) | Actual test host, OS, architecture, conditional library code | Five `LibTfm` values use one `net10.0` test project | Missing platform evidence; P3-E adds selected real hosts, not an unsupported Cartesian product. |
| P1 / P3-REPEAT (evidence) | Mutation selection, cleanup, fingerprints, receipt aggregation | F post-merge timeout; G addresses Close reliability | Historical instability addressed by G; P3-G measures new experiments and investigates regressions without changing outcomes. |
| P2 / P3-SCHEDULE (S,L) | Combined queue/Close/timeout/abort choices beyond hand-written barriers | No systematic exploration claim | Deliberate current exclusion; F is an optional decision experiment, not automatic tool adoption. |
| Trusted / P3-EXTERNAL | Stream completion, callbacks, scheduling, pool implementation, native copy, runtime/compiler | Explicit boundaries and selected integration checks | Keep trusted; no proposed proof of the platform. |

**Known defects:** no new runtime defect is established by this planning review.
F's failed empirical gate is an observed evidence failure, not proof of a library
bug. Semaphore disposal with pending waiters, callback generation ordering and
connect/dispose races are inspection hypotheses requiring counterexamples. G reports
no runtime correction; its evidence defects and test weaknesses are addressed by G
and are not open Phase 3 implementation tasks. A survivor is not automatically a defect;
an equivalent mutant needs a reason tied to the exact operation and scope.

**Deliberate exclusions:** concurrent synchronous abort may interrupt graceful
Close; no delivery after partial write; unbounded reconnect success or queue
fairness; hostile callback blocking; externally mutating payloads while borrowed;
interleaving manually supplied fragments from different application owners;
all desktop/mobile runtimes merely compatible with `netstandard`; allocator/OOM
recovery and a total process-memory bound. Byte limits do not bound the number of
queued buffers or all object overhead. An unending sequence of tiny fragments can
cost much more than its byte total; a fragment-count/backpressure policy would be
a separate product decision, not silently added to `MaxFrameSize`.

### Safety and progress must be reviewed separately

Safety assertions include no overlapping frame writes, no data after terminal
sender state, no duplicate Close reservation, no return while a delegate may
still read the buffer, no wrong-generation resource disposal, and no acceptance
past the configured accumulated byte limit. Do not make these depend on eventual
peer success. Abort is allowed to lose delivery but must not authorize unsafe
buffer reuse.

For L properties, assume runnable continuations and timer callbacks are eventually
scheduled; finite critical sections and callbacks return; the chosen stream write
settles on success/failure/cancellation or the test explicitly releases it; and
resource cleanup returns. A queued request additionally needs the current owner
to release the gate and either eventual admission or cancellation that wakes its
wait. `SemaphoreSlim` use alone is not a FIFO or starvation-freedom promise.
For reconnect progress assume a finite retry policy, eventual successful external
connect/handshake and no concurrent permanent disposal. Test fail-fast and success
paths; a system that rejects everything must not satisfy the intent.

Weak fairness of continuously enabled internal transitions can support a finite
model's eventual completion. It cannot force a peer response, an OS write or a
user callback to finish. State these environment actions explicitly; do not add
fairness to make a genuine deadlock disappear. Timer-triggered abort can finish
observation while I/O remains pending; neither a finite test nor a modeled timeout
proves a wall-clock disposal deadline.

## Alternatives and tool decision

Primary documentation checked November 7, 2026. No new tool was installed or
benchmarked for this plan; compatibility claims below are documented capabilities,
not repository trial results. Recheck a selected release and its license before
any implementation increment introduces it.

| Alternative | Capability and connection to C# | Runtime/platform, license and maintenance | Cost, limitations and recommendation |
| --- | --- | --- | --- |
| Extend current barriers, fake stream/delay and xUnit tests | Executes actual sender/transport/parser methods; explicit causal traces and isolated defect copies already work | Existing .NET 10 test host; library conditional paths via five targets. Existing dependency/license inventory; no new package required | Lowest adoption cost. Add a few named schedules and, if warranted, a small event-order table. Does not control arbitrary task scheduling. **Default.** |
| Coyote controlled scheduling | Rewrites test copies of managed assemblies and explores task interleavings; replay is useful when relevant nondeterminism is controlled | Current install docs list .NET 10/8, .NET Standard 2.0 on Linux/macOS/Windows, and .NET Framework 4.6.2 on Windows. MIT. Repository says as-is, no formal support | Requires rewrite isolation, runtime-matched host, mocks and an audit of Rx, timers, ValueTask, cancellation and semaphore interception. Trial only if P3-F's simple harness leaves a specific combinatorial gap. No production actor rewrite. |
| TLA+ with TLC | Checks invariants and temporal properties of a finite abstract state graph; expresses fairness explicitly | Java 11+ CLI, hence a separate Java toolchain on Linux/Windows/macOS subject to a chosen JRE. MIT; maintained project/release stream, with rolling and prerelease builds distinct from stable releases | New language, model maintenance and C# correspondence review. Cannot establish that handwritten C# refines the model. Defer unless a design question requires temporal reasoning beyond concrete tests. |
| More Dafny lifecycle modeling | Existing toolchain can reason about a sequential transition function | Retain pinned Dafny 4.11.0 / Z3 4.12.1 and existing Linux proof job; Dafny is MIT, with upstream versioned releases and existing repository bootstrap maintenance | A duplicate state machine would not prove Rx/async orchestration. Worth considering only for a small pure decision actually called by production, with integration and cost evidence. No async rewrite proposed. |
| Broaden Stryker scope / stress tests | Challenges assertions or samples real execution respectively; neither enumerates schedules | Existing Stryker 5.0.0; current docs require .NET 10 host, independently of application target. Apache-2.0. Current upstream docs and release activity, plus local G evidence, guide upgrades | Keep narrow named defects; G already owns Close reliability. Broad file mutation multiplies triage cost; stress cannot replace a barrier or controlled replay. No whole-library score target. |

Sources: [Coyote installation](https://microsoft.github.io/coyote/get-started/install/),
[binary rewriting and limits](https://microsoft.github.io/coyote/concepts/binary-rewriting/),
[project support](https://github.com/microsoft/coyote),
[license](https://github.com/microsoft/coyote/blob/main/LICENSE),
[release history](https://github.com/microsoft/coyote/releases),
[TLC CLI requirements](https://github.com/tlaplus/tlaplus/blob/master/USE.md),
[TLC model checking](https://nightly.tlapl.us/doc/model/tlc-options-page.html),
[TLA+ project/license](https://github.com/tlaplus/tlaplus),
[TLA+ releases](https://github.com/tlaplus/tlaplus/releases),
[Stryker prerequisites](https://stryker-mutator.io/docs/stryker-net/getting-started/),
[configuration](https://stryker-mutator.io/docs/stryker-net/configuration/),
[Stryker project/license](https://github.com/stryker-mutator/stryker-net),
[Dafny pinned release](https://github.com/dafny-lang/dafny/releases/tag/v4.11.0) and
[Dafny license](https://github.com/dafny-lang/dafny/blob/master/LICENSE.txt).

Coyote's current site and published release history need reconciling in a trial:
the latest GitHub release inspected is labeled a historical 1.7.11 republish;
its [changelog](https://github.com/microsoft/coyote/blob/main/History.md) records
.NET 8 support, while current site documentation describes .NET 10 behavior.
Do not infer the installed release implements every current-site feature, or
mistake a recent republish date for fresh implementation work. Current rewriting
docs also describe partially controlled external calls, different treatment of
finite synchronization timeouts, and no weak-memory exploration. These are
material limitations for Rx and the three/five-second disposal waits. No tested
.NET 10/Rx compatibility finding is claimed here.

### A bounded exploration proposal, only if needed

Start with a table-driven driver around real sender/transport methods, not a new
scheduler framework. Proposed finite workload: one connection, two data producers
(one two-fragment message and one single frame), one control producer with at most
one Ping/Pong, one Close request, one peer Close, one cancellation, and one abort
or failed write. Limit pending writes to two and explore at most 12 externally
controlled event releases. Enumerate legal topological orders of specified
barrier events; state the actual event graph, pruning rules and number explored.
This can be complete only for that event graph and those bounds, not internal
thread interleavings. Hitting an execution/time budget or skipping a required order is incomplete
evidence; the configured event horizon remains a limit on the claim.

If that table becomes unwieldy, P3-F may trial Coyote on **one** queued-write scenario,
with fixed seeds, explicit step/iteration budgets, recorded uncontrolled calls,
and replay of a deliberately omitted gate/release. Initially cap at 1,000
iterations, 200 scheduling steps per iteration and five minutes; these are trial
limits, not measured sufficiency. Passing iterations do not establish exhaustion.
Keep socket/Rx integration tests outside the trial. Reject a successful-looking
trial if it bypasses the actual gates/state or cannot replay the relevant defect.

An optional abstract model should distinguish message owner, frame owner,
reserved Close, actual write completion, peer notification, terminal failure,
buffer lease and disposed transport. Never collapse reservation and completion
into one atomic step. Map each modeled action to a named C# method and lock/await
boundary; identify stuttering and external actions. Require a reviewer-owned
mapping table, concrete traces for each modeled transition and replayable C#
counterexamples for relevant model faults. A changed C# boundary invalidates the
mapping until reviewed. A model that can stay green after deleting the production
write await without its correspondence checks failing provides insufficient
implementation assurance. Defer it. No refinement proof is planned.

## Proposed runtime and OS evidence matrix

The [test project](../src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj)
targets `net10.0`. [test-all-tfms.sh](../test-all-tfms.sh) varies `LibTfm`, not that
host. Installing SDKs 8/9/10 in CI does not run three hosts.

Recommend a narrow evidence-support policy, separately approved from package TFM
support. Keep building/testing all five library targets. Initially qualify .NET
10 on the three desktop/server OS families below; record actual patches and
architecture. .NET 8 and 9 are transitional host checks, not a reason to remove
published library targets. As of this research date, Microsoft's policy lists
both ending support November 10, 2026 and .NET 10 ending November 14, 2028.
Recheck at implementation time; do not turn an almost-expired runtime matrix into
permanent required CI. [.NET lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

| Placement | Actual host / OS / architecture | Library builds and scope | Decision rationale |
| --- | --- | --- | --- |
| Required PR and release push | .NET 10 / Ubuntu 24.04 x64 (qualify explicit runner image) | Existing full five-target matrix, coverage, proof and empirical jobs | Preserve current Linux gates; explicit image helps diagnose drift. Proof can keep its existing Ubuntu 22.04 pinned environment. |
| Initially manual, then required PR after qualification | .NET 10 / Windows Server 2022 x64 | `net10.0` and `netstandard2.0`: unit, lifecycle, payload, reconnect and loopback tests | Different socket/cleanup behavior and oldest conditional API path; no proof/Stryker duplication. |
| Scheduled weekly and pre-release; manual qualification first | .NET 10 / macOS 15 Arm64, if available to this repository | `net10.0` and `netstandard2.0`, same selected suites | Adds an OS and CPU family without multiplying every tool/job. Confirm hosted-runner access and actual architecture; x64 fallback is a separate recorded cell. |
| One-time qualification / manual regression while policy warrants | Real .NET 8 host + `net8.0`; real .NET 9 host + `net9.0`, Linux x64 | Same focused boundary suites, plus netstandard2.0 where affordable | Requires a compatible test host and package restore; untested with current test SDK. After end of support, retain only with an explicit user need and maintenance owner. |

The selected OS versions appear in Microsoft's
[.NET 10 supported OS table](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).
The [GitHub runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
lists these OS families and distinguishes architecture-specific labels. Actual
repository availability, billing and package/test-SDK compatibility remain
qualification tasks. Do not claim these cells pass before running them. A .NET
10 test assembly cannot become an 8/9 test by changing only `LibTfm`; a real host
project/target and matching runtime are needed. Prefer narrowly parameterizing
the existing test project if its API/package baseline permits it. Otherwise
report the unsupported cell and cost of an isolated consumer test before adding
another project. Never downgrade production targets to accommodate tooling.

Use direct `dotnet` test execution on additional OSes first. The PDD runner has
Linux `setsid`/process-group cleanup and Bash entrypoints; porting all proof and
mutation tooling to Windows/macOS is not a prerequisite. Add only enough receipt
support to identify expected cells and fail missing required evidence. Record OS
version, architecture, host framework, SDK, library TFM and loaded assembly identity;
reject silently rolled-forward or duplicate cells. Platform-qualified means these
specified tests ran there, not every compatible .NET implementation is supported.

Defer .NET Framework, Mono, Unity, browser/WASM, mobile/AOT, trimming, musl and a
full architecture cross-product absent a concrete consumer need. A netstandard
assembly's compatibility is not runtime evidence for those environments.

## Small implementation increments

Each increment gets a topic branch and PR from the then-open release branch.
Use the recorded G baseline or a later successfully gated release revision before
starting implementation. Names below use
`P3-` to avoid confusion with Phase 2 G. No increment is authorized by this
planning session itself.

| Increment | Deliverable | Dependency | Expected production/API impact |
| --- | --- | --- | --- |
| P3-A | Queued-write ownership and cancellation/abort schedules | G baseline and plan review | Prefer existing write delegate; at most a narrow internal gate-observation seam. A demonstrated waiter-lifetime defect gets a scoped fix. No public API change assumed. |
| P3-B | Pooled-buffer lease tests | A | Optional internal pool injection defaulting to `ArrayPool<byte>.Shared`; no public pool API or eager payload copy by default. |
| P3-C | Parser retained-total and scratch/metadata boundary evidence | A | Prefer fake stream inputs; optional debug invariant or small pure helper only if useful. No native-copy replacement. |
| P3-D | Two-generation reconnect and connection admission policy | A; reuse G callback/ping cases | Potential narrowly scoped generation/disposal guard only after intent review and reproduction. Public multi-subscription semantics remain an explicit decision. |
| P3-E | Qualify selected actual host/OS cells | A–D stable boundary suites | Test/receipt/workflow changes only; no production TFMs removed. |
| P3-F | Bounded schedule exploration decision | A, D; G findings | Existing event-order table first. Optional isolated tool spike; no production tool dependency. |
| P3-G | Consolidate repeatability and recurring budget evidence | B–F results (including recorded deferrals) | Focused receipt/diagnostic changes if gaps remain; no generic evidence framework. |

### P3-A — queued admission, cancellation and abort

First record intent and boundary obligations for three phases: waiting for message
ownership, waiting for frame ownership, and delegate in flight. Preserve E/G's
established Close semantics. Limit implementation to one connection, two data
sends and one control/Close contender. Use explicit entry/release signals and
record which gate is held. Include cancellation before either gate, while queued,
and after a successful first fragment; transport disposal while one writer is
held and another queued; and peer Close or read failure before a queued send is
admitted. Reuse the between-fragments test instead of copying it.

Acceptance: the schedules demonstrate successful admission as well as settled
rejection/cancellation, and assert that prohibited writes are absent. At most one
delegate executes at once;
no data reaches the delegate after terminal state. Every started task is observed
and drains after controlled owners are released/canceled, with a 15-second
**diagnostic** bound and bounded cleanup. Record behavior for a non-cancelable
queued waiter during abort; do not assume semaphore disposal wakes it. Run new
schedules 50 times on the primary host without timing-based ordering; all selected
cases must pass the ordinary five-target matrix. Fifty repeats are a smoke check,
not a failure-probability bound.

Negative experiment: in an isolated copy bypass frame serialization or omit a
required release, one at a time. Require the named overlap assertion or a
classified stuck-owner diagnostic with trace. A watchdog timeout demonstrates
missing progress only in this controlled scenario; it is not a Stryker Killed
outcome. Distinguish assertion failure, controlled deadlock, harness failure,
external process timeout, compile error and cancellation. A baseline failure blocks
the experiment. If a real defect is reproduced, reduce it and fix it within a
separate scoped commit/PR without broad lifecycle redesign.

Stop/defer: if resolving admission needs a new public cancellation/deadline
contract or concurrent subscription policy, record that decision for D and keep
A limited to established connection semantics. Never fix a test by assuming FIFO.

### P3-B — buffer lifetime across the await

Bound scope to the sender's frame buffer and caller input arrays. Use an internal
`ArrayPool<byte>` seam only if a tracking write delegate cannot observe return
reliably. Track rented identity, logical byte count and Return/clear request with
a deterministic pool; do not depend on Shared returning a particular array.
Cover success, canceled gate wait, failed/partial write, invalid text composition,
suppressed queued Pong and send-observation timeout with a deliberately pending
write. For timeout, check the lease remains held until the original delegate
settles; release it in cleanup. The trusted stream contract must prohibit use of
the array after its returned Task completes.

Acceptance: exactly one return per successful rent, no return before delegate
completion, no access after return in the harness, only initialized frame bytes
passed to the delegate, and clear-on-return requested. The [ArrayPool contract](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return?view=net-10.0)
transfers ownership on Return; clearing is conditional on the pool retaining the
array, so this is not a universal memory-zeroization claim. Preserve exact wire-byte
oracles and unchanged caller input on compliant stable inputs. Document borrowing
through completion of `SendBinary`/list sends: snapshotting an enumerable does not
snapshot arrays. Concurrent caller mutation remains unsupported unless a separate
API/copying policy is accepted. Test supported aliasing and invalid adapter inputs
without claiming all races are safely rejected.

Negative experiments: move Return before awaited send, omit Return, or pass the
rented capacity instead of written count. Each must fail a specific lease/count
assertion in a temporary source copy after a clean baseline. Run five-target
checks and 50 local repetitions of paused lifetime cases. Review any allocation
or hot-path change; no performance numbers are assumed. Defer copying, a public
ownership type or a new queue-memory limit if that is the only way to promise
arbitrary caller-mutation safety.

### P3-C — retained fragments and read ownership

Exercise the production parser through the existing stream seam, not only
`Protocol.Merge`. Bound to zero/one/multiple nonempty retained segments, empty
continuations, Ping/Pong between fragments, Close abort, invalid continuation,
cancellation/EOF, and consecutive messages. Use limits 1 and a small configured
N, checking N−1/N/N+1 across fragments; keep large integer-bound checks symbolic
or on header admission without allocating huge arrays. Check split UTF-8 through
complete-message validation and distinguish byte limits from object-count limits.

Acceptance: emitted bytes match an independent concatenation oracle; Length,
FIN, Fragment and original opcode match intent; total equals retained payload
lengths at each observable transition; control payloads do not enter the sum;
rejected over-limit input is rejected before its payload read; no stale segments
leak into the next message. Verify handshake/header scratch consumption is
sequential and immediate. Document the single-reader owner; do not create two
unsupported parser subscriptions merely to demand arbitrary thread safety.
Retain F's native-copy and masking checks unchanged unless a specific adapter gap
requires extension. Run the five-target matrix.

Negative experiments: skip total reset/segment clear, include a control payload
in the sum, or delay scratch-byte capture past the next read. Select at least two
non-equivalent defects and require named byte/metadata/read-budget assertions.
A debug assertion of total/list consistency can help local diagnosis; it does not
replace Release tests. A new pure bookkeeping proof is deferred unless it will
be called by production and costs less than maintaining these concrete checks.
Do not prove `Buffer.BlockCopy` or re-prove F's concatenation algorithms.

### P3-D — reconnect generations and admission

Specify supported sequential reconnect (`Retry`/`Repeat` with internally created
sockets), retained old Sender behavior, and connect-versus-dispose admission.
Decide whether simultaneous subscriptions on one client are supported or outside
the contract; current `Sender`/IsConnected sharing makes the choice observable.
Do not silently assert a multi-connection aggregate status policy. A finite two-
connection test is sufficient for this increment: hold old cleanup/callback,
establish the successor if the documented API permits it, then release old work.
Also hold connection setup while disposal starts and exercise cancellation.

Acceptance: distinct sockets/services for successive internal attempts; old work
cannot write to or dispose the successor transport; selected Sender/connectivity
semantics remain consistent; every accepted connection is registered and cleaned
up or rejected under the chosen disposal policy. Require both reconnect success
and failed-connect termination. Existing caller-owned socket exclusions remain.
Reuse G's observer/ping terminal tests; add only generation-sensitive interactions.

Negative experiment: route old cleanup to the current service or omit an accepted
connection's registration/removal, selecting a reachable fault after intent review.
Require a concrete identity/lease assertion, not elapsed-time inference. Run
controlled cases repeatedly and retain one real two-connection loopback test.
If the race requires a breaking public contract or a wholesale Rx redesign,
deliver the intent decision and reduced failing case, then defer the fix to its
own reviewed task; do not call the property established.

### P3-E — actual runtime qualification

Implement only the approved matrix above, first as manual evidence. Determine
whether current test dependencies and APIs can run on each intended host. Record
restore/build/run failure separately from a library failure; do not claim
compatibility from successful package restore. Use two independent clean runs
per new mandatory cell before promoting it. Retain existing Linux coverage and
all proof/mutation gates, and avoid repeating expensive tools on every OS.

Acceptance: receipt cell identity includes host runtime/patch, library TFM,
loaded assembly, OS/version/architecture and source; missing required cells fail
the gate. Matrix expectations distinguish mandatory, scheduled and not-qualified
cells. A .NET 10 result cannot satisfy an 8/9 cell. A scheduled result has its own
revision and does not silently satisfy a later PR. Verify exact current runner
labels and cost before changing workflow requirements.

Negative experiments: provide a wrong-major host probe, omit a cell, substitute
a library assembly from another target, and mix artifacts from another source or
run attempt. All must be rejected with the cell and cause identified. Reuse the
existing receipt fixtures, extending their dimensions rather than building a new
aggregator. Stop legacy-host work after one bounded compatibility investigation
(estimate: one working day) if new dependency pinning/project duplication costs
more than the near-end-of-support evidence, unless an owner identifies users.

### P3-F — decide whether systematic exploration adds value

Write the bounded event graph described above and compare it with A/D/G schedules.
Try table-driven legal event orders on one production scenario first. Measure
orders reached, discarded orders and reasons, wall time, memory, diagnostics and
replay. Use the same defect and assertions for any Coyote comparison. Cap the
whole spike at two working days (estimate); no new recurring gate before review.

Acceptance: concrete scope/configuration and event/schedule counts; baseline
passes within the proposed trial limit; the known defect is detected and replayed
on three fresh runs with identical causal ordering; all relevant waits and
external calls are accounted for. Compare unique defect/interleaving benefit with
additional maintained code, dependencies, installation and CI cost. A justified
no-adoption decision is a successful deliverable. Fixed-seed repeatability is not
proof of all schedules. If a model is explored, apply the correspondence and
mutation obligations above and report only its finite-model result.

Stop/defer Coyote if the published package cannot rewrite the required runtime,
if Rx/uncontrolled work hides the target race, or if replay depends on real-time
luck. Stop/defer TLC if mapping drifts or the model simply restates `CloseStep`
without testing the production integration. Neither tool is selected merely for
availability. Keep useful concrete regression schedules even if the tool fails.

### P3-G — repeatability, diagnostic quality and sustainable evidence

Use G's finished reliability work as the starting point. Do not repeat its fixes,
relabel Timeout as assertion detection, lower critical gates or rebuild receipt
infrastructure.
Collect three fresh runs of newly introduced A–F experiments on unchanged relevant
source/configuration and a small series of normal CI runs; report every attempt,
including failures. G already provides five independent read-failure detections
and source-bound mutation acceptance. Do not repeat that delivery as Phase 3 work;
inspect inherited results only for a new regression or unresolved outcome. Extend
existing focused receipt commands if new outcomes lack source identity or useful failure traces.

Acceptance: source/config/tool/runtime/run-attempt identity on each experiment;
separate assertion detection, survival, uncovered, equivalent/out-of-scope
review, compile error, timeout, interruption and infrastructure failure. Preserve
raw reports and per-case traces. Test cancellation and cleanup with a child that
hangs, an absent TRX and a stale source receipt; partial/missing output must never
pass. Bound cleanup even when a child keeps a pipe open, and state OS-specific
limits rather than promising Linux process-group behavior everywhere. A killed
runner cannot guarantee upload. Critical unstable evidence blocks acceptance;
rerunning is diagnosis, never a silent pass-on-retry policy.

Publish cost and triage findings, owners and next review triggers. Record human
acceptance against the reviewed source revision in PR review; machine receipts
continue to leave review obligations unsatisfied. Stop expanding mutation scope
if two consecutive trials exceed the agreed budget or introduce unexplained
critical outcome changes. Prefer a minimal isolated defect for a property that
Stryker cannot express reliably; changing any established gate needs explicit
semantic review and equivalent detection evidence, not just better CI color.

## CI placement and provisional budgets

Existing workflow: [ci.yml](../.github/workflows/ci.yml). Existing receipt/cleanup
implementation: [PDD tooling](../tools/Pdd/README.md),
[aggregation](../tools/Pdd/Aggregation.cs),
[execution](../tools/Pdd/Processes.cs),
[ordering-defect runner](../tools/Pdd/ControlledDefect.cs) and
[G boundary-defect runner](../tools/Pdd/BoundaryDefects.cs).

Measured historical reference only: F push run 42401715327 recorded approximately
340 seconds for Dafny mutations, 149 seconds for the five-target test command,
12 seconds for the ordering experiment and 491 seconds for the Stryker command.
The last command exited successfully but its behavioral evidence failed. These
are command timestamp differences from one run, not benchmarks or stable limits.
The inspected G push receipt records 339.20 seconds for Dafny mutations, 149.49
seconds for the five-target matrix, 20.89 seconds for the ordering command,
69.03 seconds for the boundary command and 564.00 seconds for Stryker. The latter
is below the local G measurement of 827.21 seconds; these different environments
are not a controlled speed comparison. Use the G push measurements as the planning
reference and subsequent samples to revise estimates; one green run is not a
repeatability study.

| Work | Placement | Provisional incremental allowance / measurement |
| --- | --- | --- |
| A/B/C/D deterministic tests | Existing PR and release-push test job | Target ≤60 seconds extra per five-target matrix; ≤15 seconds per diagnostic barrier, cleanup separately bounded. Record each case and whole-job elapsed time. |
| New isolated defect experiments | Existing controlled job where possible | Target ≤120 seconds extra total. Existing 480-second command / 10-minute job remains until measurements justify a reviewed change. Do not hide the baseline cost. |
| Windows qualification | Manual → PR after two clean runs | Estimate ≤10 minutes per job including setup; stop promotion if unstable or routinely over budget. |
| macOS qualification | Manual → weekly/pre-release | Estimate ≤10 minutes per job; access/cost unverified. Keep separate from primary gate until qualified. |
| Real 8/9 transitional hosts | Manual | Estimate ≤10 minutes each; review need at lifecycle expiry. |
| P3-F exploration spike | Local/manual, then optional scheduled | Five-minute exploration limit, 1,000 iterations/200 steps if Coyote is tried; no exhaustiveness inference. |
| Mutation/repeatability | Existing PR gates; repetition manual/scheduled | Retain 1,800-second Stryker command / 40-minute job and current Dafny budgets. Additional repeats do not run on every PR. |

Budgets above are estimates, not observed results or an instruction to increase
existing timeouts. Capture clean/cached setup separately, command duration, job
wall time, peak memory where available, runner image/CPU, worker concurrency,
failures and cleanup duration. For the first ten relevant CI runs, publish median,
maximum and sample count; a high percentile from ten runs is not a robust tail
estimate. Compare added runner-minutes and critical-path delay to G, and report
billing using the repository's actual runner plan rather than invented prices.
Target at most five extra minutes of PR critical-path time and twenty additional
runner-minutes per PR; these are provisional owner-review ceilings. Defer
noncritical scheduled breadth before weakening mandatory evidence. No automatic
path-based skip or cached evidence may satisfy an obligation for a changed source.

## Evidence and human review

For each implemented property, extend the existing manifest/boundary map only
when intent, production owner, assumptions, named tests and defect experiment
are concrete. Keep tests of C# ownership tested-only. Existing CLOSE-001..003,
REASSEMBLY and MASK proofs retain their precise scope. Capture fresh directories,
source-before/source-after hashes, exact checked commit and PR source head,
command/tool versions, actual host and run attempt. Store seed/event graph and
bounds with exploration results. Keep independent expected bytes; do not let
production helpers calculate their own oracle.

Reviewers must assess positive behavior as well as rejection, fairness and
external-progress assumptions, resource ownership, public semantics, mutation
classification, and whether a new seam changes production behavior. Expanded
trust, stronger preconditions or weaker guarantees require explicit decisions.
The contract-change report is textual assistance, not semantic approval. Record
review in the PR at the accepted revision; neither a committed checkbox nor a
passing machine receipt counts as human acceptance.

Remaining trust includes stable caller inputs, correct `Buffer.BlockCopy`, pool
and stream API contracts, generated C#/Dafny and solver correctness, Rx terminal
semantics, .NET/JIT/OS behavior, scheduling/timer progress and callback return.
Selected tests reduce uncertainty at those interfaces; they do not remove them.
A finite model or schedule set cannot establish arbitrary client concurrency,
weak-memory correctness, network delivery or unconditional liveness.

## Self-contained session prompts

Use one prompt per session. Each prompt includes the branch/review rules; the
increment text above supplies the bounded scope and acceptance criteria.

### Session P3-A

> Implement P3-A of docs/pdd-phase-3-plan.md only. Read AGENTS.md, CONTRIBUTING.md,
> this plan, the final Phase 2 G baseline/receipt, requirements/boundaries and the
> controlled Close documentation/tests. Fetch origin; use a new topic branch in
> a separate worktree from the open release branch (ask if ambiguous). Do not
> touch another session, GitVersion.yml or core.12592. Specify waiting-message,
> waiting-frame and in-flight ownership; add only the listed queued cancellation
> and abort schedules using existing production gates/delegates. Distinguish
> safety from progress assumptions; do not assume FIFO. Run the 50-repeat probe,
> isolated negative experiment and required Release five-target, coverage and
> proof/generated-source checks. Reproduce suspected defects before claiming them;
> keep any fix scoped. Preserve G gates and source-bound evidence. Record limits,
> measured cost and review decisions. Commit/push with the repository human
> identity and no AI attribution; open a PR to the selected release branch. Do
> not merge, publish or proceed to P3-B.

### Session P3-B

> Implement P3-B only after P3-A's reviewed changes are available. Read AGENTS.md,
> CONTRIBUTING.md, docs/pdd-phase-3-plan.md, its G baseline, PAYLOAD-OWNERSHIP and
> MASK-ADAPTER, sender composition and F's payload tests. Fetch origin and create
> a topic branch in a separate worktree from the open release branch. Leave
> GitVersion.yml, core.12592 and other sessions untouched. Test rent/write/return
> lifetime and initialized count across the listed bounded paths. Prefer existing
> delegates; add only an internal default-preserving pool seam if needed. Keep
> caller mutation trusted and avoid public API/copying policy changes. Detect
> isolated early-return/missing-return/wrong-count defects, run repeat probes,
> Release five-target tests, coverage, proof/generated checks and affected PDD
> checks. Report actual results/cost and remaining assumptions. Commit/push with
> human identity/no AI attribution, open a release-targeted PR, do not merge or
> publish, and stop before the next increment.

### Session P3-C

> Implement P3-C only. Read AGENTS.md, CONTRIBUTING.md, docs/pdd-phase-3-plan.md,
> G/A results, REASSEMBLY-TOTALS/PAYLOAD-OWNERSHIP and production parser/header code.
> Fetch origin; use a separate worktree/topic branch from the open release branch.
> Do not touch GitVersion.yml, core.12592 or other sessions. Test the specified
> small parser sequences, metadata, scratch-read ownership and accumulated limits
> through production adapters with an independent oracle. Reuse F's Merge/native
> and masking tests; do not replace BlockCopy or add a duplicate proof model.
> Detect at least two isolated bookkeeping/scratch defects with named assertions.
> Run Release build, five-target matrix, coverage, generated/proof and affected
> PDD gates. Record cost, exclusions and any genuine defect separately. Commit and
> push as the repository user without AI attribution; open a release PR for human
> review. Do not merge, publish or implement another increment.

### Session P3-D

> Implement P3-D only after A and G. Read AGENTS.md, CONTRIBUTING.md, the Phase 3
> plan and its exact baseline, ClientWebSocketRx/factory/service teardown and
> README reconnect guidance and error/teardown tests. Fetch origin and use a new
> topic branch/separate worktree from the open release branch. Leave GitVersion.yml/core.12592/other
> sessions untouched. Resolve sequential reconnect, old Sender and disposal
> admission intent before tests; explicitly defer an ambiguous concurrent-
> subscription public policy. Use two controlled generations and real loopback
> evidence; reuse G observer/ping cases. Require a reachable identity/cleanup
> negative experiment. Any production guard needs a reduced counterexample and
> scoped review; no broad Rx redesign. Run Release five-target/coverage/proof and
> affected evidence gates, record source-bound results and cost. Commit/push with
> human identity/no AI attribution and open a release-targeted PR; do not merge,
> publish or continue to E.

### Session P3-E

> Implement P3-E only after A–D are stable. Read AGENTS.md, CONTRIBUTING.md, the
> Phase 3 plan/G baseline, CI, test project and PDD receipt implementation. Fetch
> origin; use a topic branch in a separate worktree from the open release branch.
> Leave GitVersion.yml/core.12592 and other sessions untouched. Recheck primary
> runtime/OS lifecycle and runner availability. Qualify the approved narrow real-
> host matrix manually; do not confuse LibTfm with runtime or promise unrun cells.
> Prefer existing project/receipt seams, preserve Linux proof/mutation gates and
> classify legacy-host compatibility failures. Require two clean runs before
> mandatory promotion and negative missing/wrong-host/source/assembly fixtures.
> Record costs, OS/architecture and actual loaded library. Do not remove package
> targets. Run affected tooling tests and repository-required checks. Commit/push
> under human identity without AI attribution; open a release PR, do not merge or
> publish. Record deferrals rather than silently expanding scope.

### Session P3-F

> Evaluate P3-F only; adoption is conditional. Read AGENTS.md, CONTRIBUTING.md,
> docs/pdd-phase-3-plan.md, G/A/D results and current primary tool documentation.
> Fetch origin and use a separate worktree/topic branch from the open release
> branch. Do not touch GitVersion.yml/core.12592 or other sessions. Compare the
> specified bounded event table on production seams with at most one Coyote
> scenario if justified. Do not add a production tool dependency or recurring CI
> gate. Keep within the two-day estimated spike/five-minute exploration budgets.
> Record exact package, license, runtime compatibility, uncontrolled operations,
> bounds, detection/replay, code maintenance and cost. A TLC model, if justified,
> needs explicit C# correspondence and negative checks; do not claim refinement.
> Apply stop criteria; a documented no-adoption result is valid. Preserve existing
> tests/gates and run checks appropriate to changed files. Commit/push with human
> identity/no AI attribution; open a release PR for review. Do not merge, publish
> or automatically implement the selected approach beyond the bounded spike.

### Session P3-G

> Implement P3-G only after A–F results or explicit deferrals. Read AGENTS.md,
> CONTRIBUTING.md, docs/pdd-phase-3-plan.md, Phase 2 G's actual findings/receipt,
> current dispositions and PDD process/aggregation code. Fetch origin; create a
> separate worktree/topic branch from the open release branch. Leave
> GitVersion.yml/core.12592 and other sessions untouched. Measure repeatability
> and CI budgets on unchanged source with all failed attempts visible. Reuse G's
> reliability fixes; extend focused diagnostics/receipts only for remaining gaps.
> Exercise hanging-child, absent-result and stale-evidence negatives; never count
> timeout as assertion detection or pass by retry. Record source/runtime/run
> identities, costs, maintenance owner and review triggers. Run affected tooling
> and required repository gates. Commit/push using human identity and no AI
> attribution; open a release PR. Do not merge, publish or weaken gates without
> explicit reviewed equivalent evidence.
