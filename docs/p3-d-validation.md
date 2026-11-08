# P3-D validation and scoped review

P3-D only, based on release `6709c99e83e65de0c6f42110c47182e62f2bc4ff`,
with A and Phase 2 G merged. [Intent, counterexamples and exclusions](../verification/reconnect-intent.md)
were specified before the tests. Concurrent active subscriptions' shared
Sender/IsConnected public policy remains explicitly deferred. No P3-E work,
publication, protocol-core, GitVersion.yml or other-session change is included.
The owner explicitly authorized merge into the release after completion.

The exact G archive/receipt SHA-256 values match the Phase 3 plan:
`6701d1d713c8f4b9ec85981fbeaefd0eeddc4cbb348a642071ce628a83f47065` and
`1d7b19e709f7a2dcc8cfb4e50baa6aacd40c8c4fe42772ff8bb914f770fc4756`.
All 158 recorded hashes match git files at
`bed679ea8bd3e90ee5ce986d04c36c2a78ae9c56`. Its machine gate passed;
historical F outcomes and human-review obligations are not relabeled.

## Reproduced defects and review scope

The release baseline accepts a new loopback connection after either disposal
mode. It also lets generation 1's final status overwrite generation 2's connected
status when the successor handshakes inside the old terminal callback. The latter
test joins the actual old shared Close task before asserting the status, rather
than inferring cleanup from elapsed time. Both reduced failures are retained.

The admission lock orders registration with permanent disposal. Cleanup awaits
remain outside it. The synchronous factory now returns its service directly, and
Rx Using installs the owner before asynchronous setup. A per-subscription identity
suppresses stale final false status. Scope review retains the two status/data
branches, existing handler/sender algorithms, G observer/ping cases, internal
socket ownership and old Sender binding. No new public API, scheduler, package,
production test seam or broad Rx redesign was introduced. Callback return, Rx
resource lifetime and eventual external progress remain trusted.

The tests use two actual loopback transports and identity assertions, with finite
Retry/Repeat, retained old Sender rejection, exact successor echo bytes, delayed
old callback/cleanup, held handshake disposal/unsubscribe, and failed handshake
termination. The reachable isolated omission of old registration removal fails
`Old service retained registration` after its baseline passes. It is an artificial
evidence check, distinct from the two reproduced runtime defects.

## Local evidence before release synchronization

Checked clean commit: `b297a44ebf99250008d1f4952fd6d85f5405689e`.
Protocol/tests/controlled captures have equal before/after source identities.
All ten isolated repetitions and three fresh negative experiments passed on that
revision. Documentation/archive commits do not relabel the checked commit.

Host: Linux x86-64 Debian container, kernel 7.2.11+deb13-amd64, SDK 10.0.403,
.NET 10.0.14 test host, AMD Ryzen 9 9955HX, 32 logical CPUs. Five library targets
are not five runtime hosts. Caches were warm; mutation copies were fresh. The
three capture jobs overlapped; repetition used a separate owned worktree so no
build could overwrite its loaded binaries. Peak memory, contention and monetary
billing were not measured. No other session was controlled.

| Check | Result | Seconds |
| --- | --- | ---: |
| Release solution build | zero warnings/errors | 1.55 |
| Five-target Release matrix | 269 tests per target, all passed | 134.77 |
| Coverage run | 83.08% lines, 77.56% branches | 27.05 |
| Unchanged 70% line gate | passed | 0.01 |
| Tooling fixtures | 214 passed | 9.42 |
| Pinned proof, audit and generated-source comparison | passed | 10.77 |
| Dafny baseline plus all 15 mutations | passed, all detected | 144.57 |
| Inherited ordering experiment | detected | 4.83 |
| All 13 G boundary experiments | detected | 23.44 |
| A queued overlap experiment | detected | 4.17 |
| B lease experiments | all three detected | 12.60 |
| C retention experiments | both detected | 5.77 |
| New cleanup identity experiment | detected after baseline | 16.60 |
| Ten isolated reconnect repetitions | 10/10, nine cases plus runtime each | 100.31 |

The 45 new five-target case executions sum to 46.009 seconds; maximum single
case is 3.056 seconds. Case sums are not incremental matrix wall time. These fit
the provisional 60-second matrix allowance and the new experiment's 120-second
allowance; no existing budget was raised. G's CI matrix ran in another environment
and cannot establish a performance improvement. The repetition probe is manual.
Added maintained code includes 191 reconnect-test lines, 113 runner lines and
80 fixture lines, plus the small client/factory and existing receipt hooks.
One local/CI sample is not a recurring cost distribution; maintainers should review
normal-run cost and critical outcomes before expanding scope. Revisit on changes
to connection ownership, factory async behavior, terminal callback routing, or
if the added cost exceeds the plan's ceilings.

[Local evidence archive](evidence/p3-d/initial-local-validation.zip), SHA-256
`516708b0186e7f53600379488f203f330007050932265006ec971d55afcd721b`,
retains raw TRX/logs, source/runtime identities, exact commands, execution records,
coverage, proofs/mutations, all three negative experiments, the repetition script,
G hash check and the textual contract-change report. Receipt fixtures reject
wrong configuration/source/edit/runtime, missing results/cases, survival, unrelated
assertions, compilation failure, timeout and cancellation. Human semantic review
remains separate and unsatisfied by machine evidence.

## Diagnostic attempts retained

[Diagnostic archive](evidence/p3-d/diagnostics.zip), SHA-256
`ca419b781b88d8a3f76d5f8a5d7eed362c780359d8fad369b7f2f48c2fb5b51d`,
includes the reduced admission
and joined-generation failures, initial experimental runs and the first full local
capture at `3103e07`. That capture passed its matrix and controlled gates, but the
proof audit correctly failed: prose had been appended to the generated audit
snapshot. The prose remains in the intent review; the audit snapshot was restored
exactly. No proof/audit gate was weakened.

The first generation test incorrectly assumed subscription Dispose joined cleanup;
it failed on the still-present old registration. Joining the real Close task
instead exposed the stale false status. An initial test edit also failed compilation
because ISender.SendText returns Task, not Task<bool>; this was not behavioral
evidence. The first setup barrier signaled TCP acceptance before client stream
assignment and intermittently failed with InvalidOperationException. Moving it
after receipt of the HTTP request establishes the needed causal boundary.

The first repeat probe shared build output with matrix/coverage work. Nine processes
passed, then the tenth failed five cases with BadImageFormatException while
coverage rebuilt the assembly. The whole probe is excluded from acceptance and
retained as an orchestration failure. The corrected isolated-worktree probe passed
ten processes at 3103e07, followed by ten fresh processes on final b297a44 with
stronger exact echo-payload assertions. No failing run was silently retried into a
success claim. Validation setup also rejected the new requirement ID until the
schema was extended and rejected a misplaced boundary anchor; those were tooling
integration errors, not production or proof failures.


## Release synchronization

Before merging, a fresh fetch found release commit `931db08` (PR #117), which
reserves GitHub CI for release validation and requires complete local evidence
for routine changes. The manual workflow run 42455442198 had already been
started on b297a44 before that update was read. Its results remain separate,
not a substitute for the updated branch's required local aggregate. The release
change is preserved without altering its policy. A fresh complete local capture
will identify the combined revision and run Stryker in an isolated owned worktree.

## Final combined-revision acceptance evidence

Checked clean commit: `3d47d5c79e1be2b7e9e34f2b41908f10acfce1f6`, incorporating
release `931db08`. The complete **local four-job aggregate passed**, with identical
before/after source hashes and matching identities across all four jobs. All 14
human-review obligations remain separately not-run. The final evidence-only commit
changes documentation/archives; it does not relabel the checked revision.

All five targets passed 269 tests each; coverage is 83.08% lines. The 214 tooling
cases, pinned proofs/audit/generated comparison, all 15 Dafny mutations, inherited
ordering/G/A/B/C experiments and EPOCH-001 passed. Ten isolated reconnect processes
passed again (100.25 seconds), and three fresh identity-defect experiments passed.
Stryker 5.0.0 reports 42 Killed, 27 Survived, 7 NoCoverage, 0 Timeout,
3 CompileError and 26 Ignored in the selected scope. All 16 critical fingerprints
are Killed. No disposition, critical requirement or timeout was weakened.

| Final local command | Seconds |
| --- | ---: |
| Release build | 1.36 |
| Five-target matrix | 134.70 |
| Coverage / unchanged gate | 27.06 / 0.01 |
| Tooling fixtures | 9.44 |
| Pinned proof/audit/generated checks | 10.78 |
| Dafny mutations | 145.37 |
| Ordering / G boundaries | 4.54 / 23.45 |
| A / B / C experiments | 4.17 / 12.43 / 5.57 |
| Reconnect experiment | 16.51 |
| Stryker | 987.70 |
| Aggregate | less than 1 |

[Complete final local archive](evidence/p3-d/local-validation.zip), SHA-256
`991b152cb76973038356a2afc4b940dd88a39fffb06782792bd3658c027d16ff`,
contains the full receipt, raw evidence and repetitions; only redundant Stryker
HTML is omitted. Receipt SHA-256:
`4d5724cc4799e3ef9112e3aa44091a5176172966142757e5e330ec466a917197`.
The aggregate was run while the source checkout was clean, before adding these
archives. Stryker used its own worktree at the same commit, avoiding shared build
outputs. The final contract-change report compares against release `931db08`.

### Earlier manual GitHub run and cost limitation

[Manual run 42455442198](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42455442198),
attempt 1, completed successfully at b297a44; its aggregate also passed with all
16 critical fingerprints Killed and zero Timeout. It was a workflow_dispatch,
not automatic PR or post-merge evidence. The PR-only contract-report job was
skipped; the local contract report is retained. This extra run was started before
the release-policy update was fetched, and no further remote CI was dispatched.

[Retained pdd-receipt artifact 13607648430](evidence/p3-d/manual-ci.zip), SHA-256
`f8f67e1e64a59456cb6cd127ee6affcf26f8eb2ce1a22fcc943f0eddb7d56c09`.
Its matrix took 151.47 seconds, coverage 30.04, proof/audit 19.92, Dafny mutations
247.78, G boundaries 70.83, reconnect experiment 22.21, and Stryker 1,077.36.
Its actual test host was .NET 10.0.14. It remains evidence for its own revision,
not a replacement for the final local aggregate.

Stryker cost is a material limitation: the local 987.70-second command exceeds
C's recorded 605.52-second local sample by 382.18 seconds; the manual CI sample
exceeds G's 564-second CI reference by 513.36 seconds. Those raw differences are
above the plan's provisional five-minute critical-path increment target. They
are not controlled causal benchmarks, and no recurring percentile or billing
estimate is claimed. The absolute 1,800-second command and 40-minute job limits
remain satisfied. Under the new release policy there is no routine PR runner
charge, but full local validation still incurs this latency. Maintainers should
review Close mutation-test scope and measured cost before expanding it further;
this increment does not silently exclude the new tests or weaken existing gates
to improve timing. No P3-E or other scope expansion follows this result.
