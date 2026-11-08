# P3-G repeatability and sustainable evidence

P3-G starts from release `c27ec8e` after A–F merged in PRs #114–116,
#118–119 and #122. The open release is `releases/10.2.0.0`; this work uses
`chore/p3-g-repeatability` in its own worktree. E's Windows mandatory/macOS
scheduled promotion and continuing legacy-host qualification remain deferred.
F recommends no Coyote/TLC adoption; its bounded table stays manual. D's
concurrent-active-subscription policy remains excluded. No production API,
GitVersion, protocol source, generated code, mutation disposition, CI trigger or
gate budget is changed. Other worktrees and core.12592 are untouched.

## Inherited evidence and remaining gap

G's actual receipt archive and receipt SHA-256 match the Phase 3 plan. All 158
source hashes were independently compared with `bed679e` git objects; none differ.
Its successful machine gate does not relabel Phase 2 F's critical Timeout. G's
mixed-mutant isolation, explicit cleanup/readiness assertions and 16 mandatory
Killed fingerprints remain in force. Local Stryker uses the already-reviewed
eight-worker default from #121; hosted CI still explicitly selects two. This
increment neither retunes that choice nor repeats G's delivery as new work.

The process runner already kills Linux trees/groups and checks execution/TRX/source
identity. Its remaining gap was an unbounded output drain: a child can start a
new session, become orphaned, and retain its dead parent's output pipe. The new
regression fixture reproduced the hang for timeout and cancellation on unchanged
base runner code. Both exceeded the fixture's eight-second watchdog. These are
**cleanup failures**, not assertion-based mutation detection. The fixture then
kills its own escaped child; no other process is controlled. Raw failed TRX/logs
and reconstructed exact test inputs are retained.

Post-kill process/output drain now has a two-second bound. Cancellable pipe reads
stop after that limit; `cleanup_incomplete` and `cleanup_seconds` preserve the
result and partial logs. Timeout/interruption remains unsuccessful even when the
original parent exited zero. The regression also checks the diagnostic flag and
elapsed cleanup. Existing descendants, orphan-within-session and nested-session
cases remain covered. No new framework or package is introduced.

A reparented child that escaped the session may remain alive: the runner cannot
rediscover it from a dead parent. Linux group cleanup is not a Windows/macOS
promise. Process/pipe bounds assume responsive OS operations and local storage;
SIGKILL, a lost runner or hung storage cannot guarantee cleanup/upload. The test
owns and explicitly kills the escaped process in its finally block.

A second inspection found the older ordering/G boundary validators checked exit
code 1 and assertion TRX but did not inspect raw execution flags. A parent can
exit 1 while an inherited pipe subsequently times out. Twelve focused fixtures
reproduced 11 incorrect acceptances (timeout, interruption, incomplete cleanup,
launch error or missing execution record) and one valid positive control.
Validators now require complete raw execution evidence for baseline and mutant;
all 258 tooling cases pass. A/B/C/D's existing validators remain intact; their
shared boundary-result checks also inherit the stricter negative validation.

The first capture at `e4a5738` passed protocol, tests/coverage and controlled
experiments. Its Stryker capture was explicitly cancelled to implement this
classification correction; raw interruption and partial output are retained.
The interrupted Stryker command consumed 14.17 seconds (exit 137, interrupted=true,
timeout=false), with successful bounded cleanup. That initial capture is not a complete passing receipt and is not relabeled as
final validation. No changed-source run supplies any of the three final repeats.

F's existing runner now requires a fresh output directory, accepts `--output`,
saves command attempts before launch, and retains source identity and failed
launch/timeout/interruption details. This prevents a new diagnostic run from
overwriting the prior evidence. Its finite graph and oracle are unchanged.

## Repeatability protocol

Checked implementation: `a2dc62976a8effa922a584245b260d640a7eedcd`, clean source.
Subsequent documentation/artifact commits do not relabel that revision. Every
PDD capture records before/after hashes, command/configuration, tool/runtime and
local CI identity. Distinct fresh directories identify attempts; there is no
retry-to-pass loop. The outer attempt ledger retains start, exit and elapsed time
before moving to the next independent check, including unsuccessful commands.

One complete four-job capture supplies all repository gates. Two further controlled
captures make three fresh A–D isolated experiments on unchanged inputs. The three
E captures use only the already-mandatory local Linux .NET 10/net10.0 cell, each
running all 269 tests; they do not requalify Windows/macOS or 8/9. F’s persistence negatives additionally inject timeout/launch/interruption and
verify existing-output refusal. These are diagnostic-path tests, not real hang
or assertion detections. F builds baseline
and one isolated defect copy, then runs three fresh baseline and three defect
processes. Existing G cases are inherited checks inside controlled capture, not a
new five-read-failure delivery. No expanded recurring CI gate is added.

Host: Ubuntu 24.04.5 LTS x64, SDK 10.0.403, .NET 10.0.14, AMD Ryzen 9 9955HX,
32 logical CPUs. Package caches were warm; worktree build outputs and isolated
copies were fresh at first use. Captures ran sequentially to avoid shared-output
interference. Other session load is not measured. The outer resource ledger's
maximum RSS is cumulative across children, not a per-command allocation or a
cold-cache benchmark. F separately reports in-process peak working sets.

## Final machine checks

The complete four-job aggregate passes on the checked implementation. All 180
relevant source hashes and before/after identities match. The 14 human-review
obligations remain separately not-run.

| Check | Result | Command seconds |
| --- | --- | ---: |
| Release solution build | zero warnings/errors | 1.28 |
| Five-target Release matrix | 269 tests per target, all pass | 134.56 |
| Coverage / unchanged 70% line gate | 83.08% lines | 27.02 / 0.01 |
| Tooling suite | 258 cases pass | 14.42 |
| Pinned proof, trust audit, generated comparison | pass | 9.96 |
| Dafny baseline plus all 15 mutations | baseline pass, all detected | 143.43 |
| Stryker command | all 16 required fingerprints Killed | 346.89 |

Stryker's selected-scope outcomes are 42 Killed, 27 Survived, 7 NoCoverage,
0 Timeout, 3 CompileError and 26 Ignored. These match F's final selected outcomes;
the command remains under 1,800 seconds. Engine time and wrapper/whole-job time
are separately retained. This single fresh Stryker capture validates the existing
gate, not a new three-run mutation-scope repeatability claim.

## Repeated results and incremental cost

| Experiment | Fresh runs | Result | Measured wall seconds |
| --- | ---: | --- | --- |
| Controlled capture, including inherited checks | 3 | all baseline/named defects accepted | 64.99, 65.14, 64.73 |
| A–D commands alone within those captures | 3 | all seven injected defects detected each run | 38.13, 38.13, 38.04 |
| E Linux .NET 10/net10.0 cell | 3 | 269/269 tests each, correct loaded assembly/host | 26.97, 26.91, 26.96 |
| F baseline event table | 3 | 210/210 orders each | 0.105, 0.103, 0.096 in process |
| F isolated defect replay | 3 | named overlap assertion and identical causal prefix | 0.098, 0.113, 0.105 |

F's complete build/replay command took 3.33 seconds with warm caches. Its baseline
peak working sets were 64.11, 64.27 and 64.07 MB (decimal); these exclude compiler
memory. No extra runtime/OS or exhaustive-scheduling claim follows from these
runs. The three host probes have matching source identity and actual .NET 10.0.14
host evidence; Windows/macOS/8/9 results remain E's historical evidence, not fresh
results for this commit.

The A–D isolated commands' maximum 38.13 seconds is below the provisional
120-second incremental allowance. The whole controlled capture stays below the
unchanged 600-second hosted job budget; each command stays below 480 seconds.
The current matrix's 134.56 seconds is below G's historical 149.49 seconds, but
those different environments cannot establish a saving or a causal incremental
matrix bound. Per-case durations for all A–D tests across the five targets are
retained separately (71.81 seconds summed across five targets); concurrent test
durations are not matrix wall time and do not establish the provisional +60-second
wall-time allowance. No
before/after run on the same machine was made for production performance.

All final positive attempts passed without retry. The separate failed regression
reproductions, deliberate initial interruption, synthetic failure-path checks and
live receipt negatives remain visible with their own source/run identities.
All four live negatives reject the intended obligation: missing net10.0 TRX,
stale controlled-job source, ordering timeout with assertion/exit 1 still present,
and G read-failure interruption with its assertion/exit 1 still present.

## Outcome classification and limits

Named assertion failure after a passing baseline is detection. Survival,
NoCoverage, compile error, timeout, interruption and launch/infrastructure failure
remain distinct raw outcomes. Equivalent/out-of-scope dispositions are reviewed
exclusions bound to exact source/fingerprints, not detected mutants. No timeout
counts as assertion detection, and no percentage can waive required Killed keys.
Raw Stryker categories remain separate from Dafny verification failures and the
isolated C# experiments.

Absent TRX and stale source fixtures execute in the full tooling suite. Additional
live aggregate negatives remove a TRX or alter a source identity in independent
copies of the successful capture; the original remains intact. Each negative
must produce a failed receipt and nonzero command exit. These are rejection
checks, not replacement evidence for any missing test.

## CI cost series

The consecutive full-workflow sample starts at G's post-merge run 42408136157 and
ends at D's manual full validation 42455442198. All eight available runs are
included, including A's failed proof-mutation run and B's cancelled push. E's
three host-only trials are a different workload, already costed in its record;
they are not silently inserted to reach ten samples. The release-only policy
means ten normal full runs do not yet exist. Continue this series at the next two
relevant release/manual full runs, without dispatching redundant jobs for a
sample quota. Run metadata, jobs, receipt identities, raw command execution
records and artifact/archive digests are retained.

Durations sum actual started/completed jobs, including failed/cancelled work.
Skipped jobs consume zero here; API placeholder timestamps can be reversed and
are retained raw rather than subtracted from billed work.
Workflow wall time uses created-to-last-update, so includes queue/artifact overhead
and is an approximation to user-visible critical path, not an exact dependency
DAG calculation. Historical sources and runner images differ. Median/max describe
this finite sample, not a robust tail percentile or a causal before/after result.

| Run / source head | Outcome | Job sum (minutes) | Workflow wall (minutes) |
| --- | --- | ---: | ---: |
| [42408136157](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42408136157) / `bed679e` | success | 22.28 | 10.17 |
| [42409237201](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42409237201) / `78966de` | success | 27.55 | 15.70 |
| [42427106321](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42427106321) / `b39ff04` | failure | 20.20 | 10.45 |
| [42427757481](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42427757481) / `ed4aeec` | success | 27.32 | 15.37 |
| [42429121561](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42429121561) / `e810f72` | success | 22.63 | 13.45 |
| [42452036956](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42452036956) / `4c6fd57` | success | 28.37 | 15.08 |
| [42453088270](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42453088270) / `77af6c5` | cancelled | 17.45 | 6.93 |
| [42455442198](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42455442198) / `b297a44` | success | 30.68 | 18.88 |

All are attempt 1; PR receipts also retain their actual synthetic merge commit.
A's failure is the already-recorded inconclusive MASK-002 proof timeout, not a
new detected assertion. B's cancelled run has incomplete Stryker identity and a
failed evidence receipt. Later successes never replace either outcome.

For n=8, job-sum median/max is 24.98/30.68 runner-minutes; workflow-wall median/max
is 14.27/18.88 minutes. Job medians/maxima in seconds: tests 221/234, controlled
112.5/178, Stryker 835.5/1104, protocol 337.5/416, aggregate 18.5/26. Cancelled and
failed jobs are included; these figures must not be interpreted as successful
execution percentiles. G's own job sum was 22.28 minutes and workflow wall 10.17.
The maximum observed increases are 8.40 runner-minutes and 8.72 wall minutes:
the former is below the provisional +20 ceiling; the latter exceeds +5 and
requires owner review before any recurring scope expansion. This is an observed
cross-revision difference, not evidence that A–D alone caused the increase.

The first two receipts (G push and planning-only PR) have identical sets of 158
relevant source hashes, but workflow wall times differ by 332 seconds. Thus even
this unchanged-source pair exceeds five minutes of variation; runner conditions
and PR/push job differences were not controlled. It does not establish a cause
or excuse an over-budget future run. The later A–F unchanged-source repetitions
in this study are local, not fresh hosted qualification.

The repository is public and these jobs used standard hosted runners, as recorded
in E's qualification. No new hosted runs or paid runner selection is introduced here. The new tooling
regressions add a bounded local/release fixture cost; routine PR CI remains off. Actual invoice/billing data was not accessed; job-seconds
are resource measurements, not a monetary estimate. Cold setup, runner image,
worker count and source differences prevent treating local speed as a CI saving.

## Maintenance and next review

Repository maintainer Jasper H. Bojsen owns the process/receipt tooling, existing
mutation dispositions and the decision to expand recurring evidence. Review this
PR at its exact source revision; machine receipts leave every human-review
obligation unsatisfied. This document is not human approval.

Revisit on any critical timeout/survival, unexplained fingerprint change, pipe
cleanup regression, process-tree/platform change, runtime/runner or tool upgrade,
source-identity/aggregation change, or graph/pool/gate ownership change. Preserve
and triage the first failure; a rerun is a separate diagnostic attempt. Two
consecutive over-budget or unexplained critical-outcome trials stop scope growth.
Require reviewed equivalent detection evidence before changing any gate.

Continue the CI series at samples nine and ten and report n/median/max including
failures; do not invent a high percentile. Review the observed +5-minute ceiling
breach before adding recurring work. Prefer deferring noncritical scheduled
breadth; do not weaken mandatory proof, test, coverage or fingerprint gates.
Reopen Windows/macOS or legacy-host policy only under E's review conditions.
No merge, publication, new tool adoption or automatic scheduling is authorized
by these machine results.

## Reproduction commands

From the checked revision, use fresh output directories:

```sh
./scripts/pdd.sh capture --job protocol --output artifacts/p3-g/protocol
./scripts/pdd.sh capture --job tests --output artifacts/p3-g/tests
./scripts/pdd.sh capture --job controlled --output artifacts/p3-g/controlled
./scripts/pdd.sh capture --job stryker --output artifacts/p3-g/stryker
./scripts/pdd.sh aggregate --output artifacts/p3-g
# Repeat controlled twice into fresh directories; repeat this host cell three times:
./scripts/pdd.sh test-host --cell linux-10-net10.0 --output artifacts/p3-g-host-1
python3 verification/exploration/run.py --output artifacts/p3-g-exploration
```

Local captures use the existing 1,800-second outer-command default; isolated
experiments retain their own 180-second bounds. Every observed controlled command
is additionally compared with hosted CI's unchanged 480-second command and
600-second job limits. This is not a claim that the local default is CI's budget.
The repeat captures use identical commands/configuration and fresh directories.
The retained driver/negative scripts record the exact paths and invocations used.

## Retained evidence

[Study archive](evidence/p3-g/study.zip) retains the final four-job receipt,
all three controlled and host captures, F baseline/replay traces, per-case timings,
raw execution/TRX/source/runtime records, the attempted-command ledger, environment
information, initial interrupted capture, both failed regression reproductions,
negative receipt inputs/results and the eight historical CI receipt/cost records.
Sparse negative directories describe their edits against the unchanged `full/`
positive capture; `live-negatives.py` reconstructs them. Only redundant Stryker
HTML is omitted. The historical CI records retain artifact IDs and archive/receipt
digests; hosting retention may expire independently of these retained extracts.

Archive SHA-256: `6b9919e2368cf7853bd5cbb4c24f4033adc03d6dc355ae1cf85508957247e93d`.

Final receipt SHA-256: `14683bf455e60d0282df0783006aadcf4bc86ab090bd3dca02636d1baae1578f`.
