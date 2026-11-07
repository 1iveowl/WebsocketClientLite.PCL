# Increment G validation and diagnosis

This is machine evidence and source-based review rationale. Human semantic
acceptance remains a separate revision-specific PR decision.

## Failure diagnosis

The original failed post-F receipt is retained verbatim in
[the G evidence directory](../verification/evidence/increment-g/original-receipt.zip).
[Local diagnostic logs and reports](evidence/increment-g/diagnostics.zip) retain
commands/configuration, hashes, timestamps, tool/runtime identities and raw
outcomes. Diagnostic ZIP SHA-256:
`7a30cd3102080e380cc98ed39e083156353a8f0d000ec5df77575bd8a8876455`.
They are baseline-revision experiments, not final G receipts.

The critical source/operator fingerprint is recorded in
[the G design record](../verification/close-evidence-g.md). Numeric ID 590 is
only meaningful inside the original report.

| Baseline experiment | Observation |
| --- | --- |
| Original release CI, four workers | 490.54 s command; critical statement **Timeout**; evidence gate correctly failed |
| Exact statement alone, normal host | critical **Killed**; selected mutant session about 6 s; total 98.82 s including build and coverage |
| Full original configuration with debug logging | critical **Killed**; 464.57 s total; it shared a session with missing Pong and missing incoming-Close status |
| Same implicated statements, `DOTNET_PROCESSOR_COUNT=2` | mixed group exhausted 41.196 s, remaining pair exhausted 35.192 s, then isolated critical assertion killed; 197.41 s total |
| Exact statement, `DOTNET_PROCESSOR_COUNT=1` | critical **Killed**; failure detected around 6 s, session cleanup continued; 126.38 s total |
| Source-deleted stress loop alone, iteration diagnostics | survived; it is not independently a detector of readiness, and alone did not reproduce timeout |

These reproduce exhaustion of the mixed experiment's budget and a large
schedule-dependent delay to the critical assertion. They do **not** reconstruct
the exact scheduling of the historical CI timeout: the retained local reports
ultimately classify the critical statement as Killed. The CI log has no per-test
trace sufficient to establish that exact interleaving. No production runtime
failure was identified. The original CI Timeout remains inconclusive evidence.

The execution design had three avoidable dependencies: unrelated mutated waits
shared a VSTest session, four workers oversubscribed a runner recommending two,
and the 200-iteration error-routing test did not explicitly release/drain terminal
cleanup after its assertion. G isolates mutants, uses two workers, releases the
stress peer wait only after checking error routing, and adds a direct lifecycle
assertion that does not need socket progress or a timer to detect missing readiness.
The full stress test and all real loopback tests remain selected.

## Acceptance experiments and budgets

`boundary-defects` runs a passing baseline, then nine exact isolated defects,
with five independent process repetitions for NotifyReadFailure (13 experiments).
Every mutant must exit 1 with its specified assertion in TRX. Compile failures,
timeouts, missing cases/repetitions and unrelated assertions fail the gate.

The existing seven critical Stryker keys are migrated by exact source snippet,
operator and replacement to the new source hash. The migration normalizes
whitespace and the extracted delegate name (`CloseOnceAsync` → `closeOnceAsync`);
the seven inherited critical source statements themselves are unchanged. Nine further keys are required
Killed. The original controlled ordering-defect gate remains independent. An
aggregate mutation percentage cannot waive any of these checks.

Budgets remain 30 seconds additional per Stryker session, 30 minutes per Stryker
command and 40 minutes per CI job. Controlled commands retain 480 seconds and a
10-minute job. The new direct tests use three-second diagnostic waits with explicit
barriers and bounded cleanup. No budget increase or Timeout-to-Killed conversion
is used.

The exploratory G run took 486.59 s and killed all seven inherited critical keys.
It exposed a test weakness: awaiting a virtual tick's effect could also accept a
real timer tick. The final test requires write entry synchronously at virtual
advancement; scheduler substitution is independently required Killed.

Final source-bound CI receipts identify the exact tested commit. The delivery
report links the release-branch run and its evidence gate after merge.
All five library targets use a recorded .NET 10 test host; a broader runtime
matrix, native-copy/compiler/runtime trust and exhaustive async verification
remain outside G. Other custom-context continuation sites and exact callback
progress remain explicit gaps. See the fingerprinted dispositions and
[review notes](../verification/close-intent-review.md).

## Local source-matched validation

The Release solution build passed. All five library targets passed 220 tests each
on the actual .NET 10.0.14 test host. Coverage was 82.2% lines and 76.2% branches;
the unchanged 70% line gate passed. Pinned proof, trust audit and generated-code
checks passed; all 15 targeted Dafny mutations were detected. All 162 tooling and
receipt failure-fixture tests passed.

The ordering experiment passed in 4.23 s. The 13 boundary experiments passed in
22.64 s; the five read-failure mutation test-case durations (including cleanup) were 41.62, 41.56,
37.52, 38.61 and 39.50 ms. These are assertion failures with exit code 1, not
process timeouts. The full controlled command retains substantial margin within
480 s and the ten-minute CI job, without changing either budget.

The complete local Stryker command took 827.21 s (13m 47s including tool restore);
the Stryker engine reported 826.86 s. Selected outcomes were 40 Killed, 29 Survived,
7 NoCoverage, 0 Timeout, 3 CompileError and 26 Ignored. All 16 critical fingerprints
were Killed. The source-matched aggregate passed, while all human-review items
remained not-run. This is comfortably within the unchanged 30-minute command
and 40-minute job budgets; two workers trade throughput for isolation and lower
contention.

A final test guard also requires the injected virtual scheduler to own a scheduled
item before its tick, so an accidentally coincident real timer cannot satisfy the
checkpoint. Targeted tests and boundary defects are rerun after that guard; final
CI captures bind all checks to the committed revision. Earlier local receipts
retain their original identities and are not relabeled.

[The complete local capture](evidence/increment-g/local-validation.zip) retains
raw JSON/TRX, logs, hashes, command receipts and source identities (the redundant
HTML mutation rendering is omitted). ZIP SHA-256: `3aefae3ca68d937427ddc718d8998ec999b6f3421ba42c8c91e3090944428e0a`.
