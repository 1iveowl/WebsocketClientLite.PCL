# P3-F bounded exploration decision

Recommendation for review: **do not adopt Coyote or TLC at this time**. Retain the
manual event table as a bounded spike, with no recurring CI gate or production
package. No new production defect was found. The injected frame-capacity defect
is the same assertion challenge used by A, not a newly discovered library bug.
Adoption and any further implementation remain the owner's decision.

Base: fetched release `3abb948` on 2026-11-08, topic
`chore/p3-f-exploration`, separate `/tmp/ws-p3-f` worktree. Historical release
branches remain on origin; the plan and current A–E records identify
`releases/10.2.0.0` as the open development release. Read AGENTS.md,
CONTRIBUTING.md, the Phase 3 plan, G's design/validation, A's ownership/validation
and D's intent/validation records. Their past results are inherited context, not
fresh evidence from this experiment. In particular G's historical Timeout remains
inconclusive, A's abort fix remains intact, and D's generation policy is not
broadened. No GitVersion, protocol core, other-session, production or gate change.

## Why no adoption now

The decision criterion is additional assurance per unit of maintained machinery.
The existing production seams were sufficient for this bounded question: the
small event table passed 210 orders and detected/replayed the selected overlap
fault in three fresh processes. It therefore did not reach the plan's trigger
for trying Coyote: a specific combinatorial gap that makes the table unwieldy.

This is a conservative investment decision, not a comparative benchmark win.
Coyote was not executed, so the experiment cannot show that it would find no
additional bugs, run slower, or fail on this runtime. No new defect was found,
and the injected fault already belongs to A's assertion challenges. Its value
here is checking the table's detector and causal replay, not demonstrating novel
bug discovery. The 210 orders cover only the declared graph; uncontrolled
continuations and excluded external orders remain gaps.

Coyote could add internal scheduling choices. To justify adopting it, a follow-up
should identify a concrete race or missed ordering and demonstrate better
repeatable detection than the table on the same production paths. In exchange,
the repository would maintain a qualified package/runtime pairing, isolated
assembly rewriting, an audit of uncontrolled operations and replay artifacts.
The published-package/current-documentation discrepancy is an unresolved setup
question, not proof of incompatibility. Those costs are concrete obligations;
the additional assurance benefit has not yet been measured.

TLC addresses a different problem: exploring an abstract design, including
conditional progress. This spike exposed no temporal-design question requiring
that abstraction. A second state machine would require reviewed C# correspondence
and negative checks before its results could inform implementation assurance.
A green model by itself would not validate the production await/gate behavior.

Retain the bounded manual experiment without a recurring gate. Reconsider Coyote
for a specific internal race, luck-dependent replay or unmanageable event graph;
reconsider TLC for a concrete temporal-design question. Require a measurable
benefit and the original budgets before adoption. This conclusion does not
remove existing tests, narrow their obligations, or claim general concurrency
correctness.

## Event graph and production correspondence

[Program.cs](../verification/exploration/Program.cs) calls the actual sender and
transport. No model or replacement semaphore implements the baseline algorithm.
It uses the existing friend assembly name in a standalone executable outside the
solution, existing write delegate, and A's reflection-based MemoryStream setup.

| Event | Production operation and barrier |
| --- | --- |
| F (fixed prefix) | `SendText(IEnumerable<string>)` / `SendList`: two fragments, await entry of first write delegate with message and frame gates held |
| D | `SendText(string, token)`: second producer, one frame |
| C | Cancel that token and observe the second producer settling before admitting another contender |
| P | `SendPing`: one control request, bypasses message gate, uses real frame gate |
| R | Release write delegate barrier; join fragmented producer (success or terminal failure) |
| H | `SendCloseHandshakeAsync`: one request; reservation still occurs inside `WriteFrameSerializedAsync`, before delegate completion |
| X | `NotifyPeerClose`: external peer notification |
| A | `TcpConnectionService.Dispose`: abort; does not force the paused delegate to finish |

Graph: F precedes everything, and D < C < P < H. R, X and A have no additional
edges. Enumerate all 7! = 5,040 permutations, reject 4,830 violating those edges,
and execute all 210 remaining orders. Eight externally driven events including F
are below the 12-event ceiling. One connection, two data producers (two fragments
and one frame), one Ping, one Close, one peer Close, one cancellation and one abort.
At most two requests wait behind the active frame: after C settles D, Ping and
Close may queue together. This is a bound on queued requests, not two total
outstanding calls including the owner. There is no failed-write variant.

The D/C/P/H edges deliberately narrow the graph to bound queue depth; they are
not claims that other application orders are illegal. R joins the fragmented
producer before the next external event. This excludes external events between
its resumed fragments. A single release also lets later delegates finish freely;
.NET chooses their continuations. Counts describe external orders, not distinct
internal schedules or an exhaustive concurrent state space. Full production
traces may differ after R. No fairness, wall-clock disposal deadline or weak-memory
claim follows from enumerating this table.

Assertions: no overlapping frame delegates, at most one Close write, no data
entry observed after peer Close notification, all observed calls settle within
three seconds, and an explicit successful order `RDCPHXA` emits opcodes
`1,0,1,9,8`. The positive case prevents a reject-everything implementation from
passing. Expected cancellation/disposal/sender exceptions are recorded separately
from success; an unexpected exception or timeout fails. The peer assertion is a
sampling assertion at delegate entry, not proof that reservation and entry are
atomic. Cleanup releases the barrier, cancels the token and joins observed tasks
with a three-second bound; Python bounds every child at 300 seconds. A timeout is
never accepted as defect detection. No real socket traffic or timer delay drives
these orders.

## Detection, replay and cost

Run manually from the repository root:

```sh
python3 verification/exploration/run.py
# Replay one baseline order after building:
dotnet verification/exploration/bin/Release/net10.0/WebsocketClientLiteTest.dll DCPRHXA
```

The runner builds baseline, executes three fresh processes, then builds one
isolated temporary source copy changing only `_writeGate = new(1, 1)` to
`new(2, 2)`. Three new processes replay `DCPRHXA` and require the named
`Overlapping frame delegates` failure plus the identical causal prefix:
F → first delegate enters → D → C → single cancelled → P → Ping delegate enters
→ R. Ping entry before R means the first delegate still owns the frame. Completion
ordering after R is not required to match. No live source or installed binary is
mutated. Child core dumps are disabled. Compile errors, unrelated failures and
missing count/assertion records do not pass the runner.

Raw [command records](evidence/p3-f/commands.json),
[source identities](evidence/p3-f/identity.json), baseline traces and defect logs
are retained in [the evidence directory](evidence/p3-f). Source hashes before and
after match; the base revision plus those hashes identifies the uncommitted spike
inputs at execution, not a falsely claimed clean-commit receipt. Each baseline
passes 210/210; all three negative runs detect and replay the causal overlap.
Host is .NET 10.0.14, SDK 10.0.403, Ubuntu 24.04.5 LTS x64. This is net10.0 library
execution only, not Coyote runtime qualification or additional OS qualification.

Initial setup build took 2.72 seconds with warm package caches. Repeated cached
build, isolated-copy build, process wall times and measured in-process exploration
seconds are recorded individually. Exploration is approximately 0.1 seconds per
210-order process with roughly 64 MB peak working set; this excludes compiler
memory. Six exploration/replay processes total less than a second of wall time.
No cold-network restore benchmark, CPU contention measurement or cloud billing
measurement was made. Work stayed within the two-working-day estimated spike and
five-minute exploration ceilings. Added runner minutes and PR critical-path time
are zero because no workflow invokes it; manual build/run cost is reported, not
an invented monetary price.

## Alternatives and stop decision

Primary documentation rechecked 2026-11-08:

- [Coyote install](https://microsoft.github.io/coyote/get-started/install/) lists
  .NET 10/8, .NET Standard 2.0 and Windows .NET Framework 4.6.2 support.
- Exact published candidates inspected: [Microsoft.Coyote 1.7.11](https://www.nuget.org/packages/Microsoft.Coyote/1.7.11)
  and [Microsoft.Coyote.CLI 1.7.11](https://www.nuget.org/packages/Microsoft.Coyote.CLI/1.7.11).
  Gallery assets describe net6.0/netcoreapp3.1 (plus netstandard2.0 for the
  library); computed net10 compatibility does not establish a net10 rewrite host.
- [Rewriting documentation](https://microsoft.github.io/coyote/concepts/binary-rewriting/)
  requires a host matching the target major, explains partially controlled calls,
  treats nonzero finite synchronization waits as infinite during systematic
  exploration, and excludes weak-memory behaviors. These current-site statements
  are not verified capabilities of the historical package.
- [License](https://github.com/microsoft/coyote/blob/main/LICENSE): MIT.
  [Project support](https://github.com/microsoft/coyote): provided as-is without
  formal support.

No Coyote package was installed or rewritten, so runtime compatibility, intercepted
API coverage, iterations, detection and replay for Coyote are **not run**. The
package/site discrepancy is unresolved, not a demonstrated incompatibility.
The table remained small and already reproduced the selected defect deterministically;
there is no measured unique defect benefit to justify even one Coyote scenario.
Stop at that adoption criterion. If reopened for a specific internal race, limit
the trial to one scenario, 1,000 iterations, 200 steps and five minutes, require
matching-host rewriting and the same assertions/defect, and reject uncontrolled
race or luck-dependent replay. Do not substitute a source-built unreleased tool
or older production target without a separately reviewed scope decision.

Uncontrolled/trusted operations in this table: .NET thread-pool continuations,
SemaphoreSlim fairness and cancellation wake-up, lock/Interlocked behavior,
ArrayPool and masking randomness, status callbacks (no-op here), and runtime/JIT.
The delegate replaces external stream progress; MemoryStream and an unconnected
TcpClient are disposed normally. Three-second Task.WaitAsync is only a diagnostic
watchdog. Rx subscriptions, timers, parser routing, real I/O/TLS, connection-handler
Close delays and cross-generation callbacks are absent. A Coyote trial would need
to audit Task/ValueTask, semaphores, cancellation and these external boundaries;
this record makes no interception claim. G and D integration tests remain needed.

TLC is deferred without a model or package: no outstanding design question here
requires temporal modeling beyond these concrete orders. Maintaining a second
state machine and C# correspondence would add work with no measured benefit.
No model checking or refinement claim is made. Any later model must separately
map reservation/completion and every lock/await boundary, show concrete transition
traces and negative C# correspondence checks, including removal of the write await.

Compared with A, the table combines queued cancellation, two-fragment ownership,
Ping/Close and peer/abort order in 210 cases; it does not replace A's detailed
outcome assertions or discover a new defect. G covers observer/ping/timer teardown
that is absent here. D covers two generations/real sockets absent here. Coyote
could explore internal continuations, but that benefit remains unmeasured; TLC
would only explore an independently maintained abstraction.

Maintenance is the small manual C# driver, project and Python isolated-copy runner,
plus review of event edges, expected outcomes, private `_stream` reflection and
exact mutation anchor when production changes. No new NuGet dependencies: the
project inherits the production references, and uses Python's standard library.
The repository maintainer owns any decision to retain or expand it. Revisit only
for a concrete unexplained internal race, replay instability, gate/seam changes,
or graph growth that exceeds the stated budget. Human acceptance must reference
the PR revision; this document does not supply it.

## Validation scope

Maintained executable/configuration code totals 182 lines: 100 C#, 12 project XML
and 70 Python. The final three baseline process exploration times are 0.103,
0.092 and 0.105 seconds; measured peak working sets are 64,258,048, 64,385,024 and
64,520,192 bytes. Negative process exit -6 is the unhandled named assertion
exception, captured with its JSON failure trace, not a timeout classification.

The first matrix invocation supplied CONFIGURATION as an environment variable,
which this script does not consume, and therefore ran Debug. It is retained as
an additional check, not called Release evidence. The subsequent explicit
`./test-all-tfms.sh Release` is the required Release matrix. Debug and Release
used separate build outputs; the isolated defect copy used its own temporary tree.
No shared test output from another session was used.

At initial PR creation, merge was not requested. Full proof, mutation and four-job
aggregate checks were not rerun for these standalone experiment/document files;
no source selected by those obligations or gate configuration changes. The
repository's full-local-evidence requirement before merging remains in force.

Completed checks: Release solution build passed with zero warnings/errors (2.04
seconds); both Debug and Release five-target matrices passed 269 tests per target;
coverage execution passed 269 tests with 83.1% lines and 77.6% branches, satisfying
the unchanged 70% line gate. Raw build, matrix and coverage logs are retained beside
the exploration records. Python syntax, local document links, source-hash equality
and `git diff --check` passed. No proof/mutation/aggregate success is claimed for
this revision. Those existing checks and requirements are preserved.
