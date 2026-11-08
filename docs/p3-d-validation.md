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
