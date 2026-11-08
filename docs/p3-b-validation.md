# P3-B validation and review record

P3-B only; no later increment or publication. Implementation starts from release
`e810f72d04cf2916f5e3b5125458fc5b6cf4905d`, containing merged P3-A PR #114.
That PR records final source-bound machine acceptance and owner-authorized merge;
human review of this new increment remains separate. The specification is P3-B in
`docs/pdd-phase-3-plan.md` at planning commit `78966de`, read from its planning
branch without importing later increments. See [lease ownership](../verification/payload-lease-ownership.md).

## Baseline and production scope

The retained G post-merge receipt archive was read again. Archive SHA-256
`6701d1d713c8f4b9ec85981fbeaefd0eeddc4cbb348a642071ce628a83f47065` and receipt.json
SHA-256 `1d7b19e709f7a2dcc8cfb4e50baa6aacd40c8c4fe42772ff8bb914f770fc4756`
match the plan's run 42408136157 / artifact 13588159091. Its four-job machine gate
passes at `bed679e`; this does not relabel F's earlier failed evidence.

No production lifetime defect was reproduced. The only runtime change is an
internal optional ArrayPool argument defaulting to Shared, a stored reference,
and using it for Rent/Return. No public signatures, payload copying policy,
protocol/generated code, gate budgets, Stryker dispositions, GitVersion.yml or
other sessions changed. The original checkout's core.12592 was left untouched.

## Local evidence

Checked clean commit: `5ccbcdbc87f3d132b426fdbed0eb467b91936934`. All four captures
and the 50-repeat probe bind the same relevant source hashes before and after.
Later documentation/archive commits do not relabel that checked revision. PR CI
will capture its own source/merge revision.

Host: Linux x86-64 Debian container, kernel 7.2.11+deb13-amd64, AMD Ryzen 9 9955HX,
32 logical CPUs, SDK 10.0.403; actual test host .NET 10.0.14. Five library targets
are not five runtime versions. Package caches were warm. Defect copies were fresh.
Protocol, matrix and controlled jobs overlapped; Stryker and the probe used separate
owned detached worktrees at the same commit to isolate build outputs. No other
session was controlled. Contention, peak RSS and monetary billing were not measured.

| Check | Result | Measured seconds |
| --- | --- | ---: |
| 50-process lifetime probe | 50/50; 750 cases including runtime | 320.31 |
| Release solution build | pass; zero warnings/errors | 2.15 |
| Five-target Release matrix | 247 tests per target, all pass | 134.62 |
| Coverage run | 82.24% lines; 76.37% branches | 27.01 |
| Unchanged 70% line gate | pass | 0.01 |
| Tooling fixtures | 186 pass | 9.83 |
| Pinned proof, trust audit and generated-source check | pass | 10.72 |
| Dafny baseline plus all 15 mutants | baseline passes; all detected | 143.83 |
| Inherited ordering experiment | named assertion detected | 5.47 |
| Inherited 13 G boundary experiments | named assertions detected | 22.74 |
| Inherited A frame-overlap experiment | named assertion detected | 4.07 |
| Three new lease/count experiments | named assertions detected | 12.40 |
| Stryker 5.0.0 command | 41 Killed; 28 Survived; 7 NoCoverage; 0 Timeout; 3 CompileError; 26 Ignored | 859.98 |

The complete four-job aggregate passes, including LEASE-001-TEST and all 16
required Stryker fingerprints. All 13 human-review obligations remain not-run.
Stryker is within its unchanged 1,800-second limit. One historically out-of-scope
synchronous-disposal mutant is additionally killed in this run; this does not
establish a new general abort guarantee or change any disposition.


The new 70 matrix case executions sum to 25.327 seconds; 25.115 seconds belong to
the real five-second observation timeout across five targets. All other new cases
sum to 0.212 seconds. These are per-case timings, not added matrix wall time:
xUnit runs classes concurrently. The new 12.40-second isolated command is within
the provisional 120-second allowance. The matrix's 134.62 seconds and G CI's
149.49 seconds are different environments, not a performance improvement claim.
No before/after allocation or hot-path benchmark was performed.

The 50-process probe took 320.31 seconds, with median 6.391 and maximum 6.846
seconds per process (n=50); all 14 cases and the runtime probe passed in each.
It includes the real timeout and remains manual. This is not a ten-run CI cost
series or a tail-latency guarantee. No existing command or job budget increased.

[Complete local capture](evidence/p3-b/local-validation.zip), SHA-256
`02b282d6023a76f20fbe549d79f6bf72d4ea18638017b60b98e8db962e6df647`.
It retains raw JSON/TRX, logs, source/command/execution/runtime identities,
coverage, proof/mutation results and the contract-change report. Only redundant
Stryker HTML rendering is omitted. Receipt.json SHA-256:
`fd871488515f1a044829a8d345d137f81d03ee52ee1de9baf68d63fc5cfd6301`.

[Initial diagnostics](evidence/p3-b/diagnostics.zip), SHA-256
`a89d0d538a53dbe9ea4172065ae4402b679f5c2bf33ffa13fdb0d2b94cdac905`.

## Diagnostics and limits

The initial test compilation failed because the fixture assigned read-only
Dataframe.Binary; it was corrected to the existing Payload initializer. No TRX
was produced for that compile failure. Manifest validation initially rejected the
new LEASE ID family, then the noncanonical review heading. The schema was extended
and the heading corrected without weakening existing checks. The second targeted
run passed 14 cases; the initial isolated defects and first tooling run passed.
Those dirty-source diagnostics are retained separately, not substituted for the
clean final captures. No timeout or unsuccessful attempt was relabeled as detection.

LEASE-001 remains tested C# ownership. The three isolated edits fail named
assertions after a passing baseline: early return after starting but before awaiting
the writer, missing return, and rented capacity passed instead of initialized count.
Execution/TRX/source/runtime checks reject incomplete evidence, unrelated failures,
timeouts, compile failures and stale edits. F's independent vectors, invalid adapter
input checks, and G/A gates remain intact. The contract report is a textual review
aid and cannot provide semantic acceptance.

Callers keep borrowed binary arrays stable through completion, including repeated
references in a list; enumerable snapshotting does not copy bytes. Write delegates
must relinquish the frame when their Task settles. Pool compliance, conditional
clear-on-return semantics, runtime/Rx correctness, scheduling, returning callbacks
and eventual external write settlement remain trusted. Observation timeout does not
transfer ownership or prove delivery. No universal zeroization, arbitrary mutation
safety, queue-memory limit, fairness or exhaustive concurrency claim is made.

Review the default-preserving pool seam and these assumptions against the PR source
revision. Revisit recurring cost after normal CI samples or changes to pool/write
ownership; human semantic review remains pending independently of machine results.
