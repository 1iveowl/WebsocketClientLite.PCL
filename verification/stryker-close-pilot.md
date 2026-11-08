# Stryker.NET Close boundary pilot

This pilot adds empirical mutation testing for handwritten C# alongside the Dafny proof mutations in [PDD phase 2](../docs/pdd-phase-2-plan.md). Stryker changes C# expressions and statements and runs tests against the resulting assemblies. It tests the strength of the test suite; it does not prove the implementation or validate a Dafny contract.

## Run

From the repository root with the .NET 10 SDK:

```bash
./scripts/stryker-close.sh
```

The script restores Stryker.NET 5.0.0 from the repository-local tool manifest, uses the checked-in test-project configuration and writes timestamped HTML/JSON reports under `artifacts/stryker-close/`. Each invocation also keeps a unique console log there; the dots reporter exposes progress in redirected output. The artifacts directory is ignored by git. The pinned tool's command is `dotnet tool run dotnet-stryker -- ...`.

The pilot selects the complete `Service/WebsocketConnectionHandler.cs` file. This includes connection setup and control-frame handling as well as Close/disposal orchestration. Selecting the file avoids fragile character-offset scopes; findings outside Close must be classified separately. Generated `.g.cs` files are excluded. Sender state transitions, other C# files and other library targets are not mutation-tested by this configuration.

Run the full existing test project so tests outside `TeardownTests` can detect defects. The mutation run uses the net10.0 library target and test host, Release configuration, Standard mutations and four independent test workers. Override this with `./scripts/stryker-close.sh --concurrency 1` when sharing a constrained machine. Parallel workers shorten mutant execution, but build, initial tests and coverage collection still add overhead; measure timeouts and resource contention before increasing concurrency further. This does not replace the ordinary five-target test matrix. An initially failing test run stops the experiment.

The score break threshold is deliberately zero during the pilot. A successful command means the experiment completed, not that every mutant was detected or that a requirement is satisfied. Preserve killed, survived, uncovered, timeout and compile-error outcomes separately. A timeout is not evidence that a behavioral assertion detected the bug, even if Stryker's headline score treats it as detected. Inspect the JSON report and the associated tests before making an assurance claim.

## Use in later increments

This is preparatory tooling for the existing phase 2 increments, not a replacement for increment B.

| Increment | Required follow-through |
| --- | --- |
| B: Dafny mutations | Keep the targeted proof-obligation runner. Do not substitute Stryker results for verifier rejection. |
| C: boundary register | Link applicable C# mutations and tests to the Close boundary assumptions. Identify findings outside Close separately. |
| D: receipts | Add a separate empirical-mutation evidence section consuming Stryker JSON, recording revision, config/tool versions, target/runtime and raw outcome counts. Never combine its score with proof-mutation outcomes. If D lands first, add this integration with E. |
| E: controlled schedules | Rerun this pilot after adding deterministic Close schedules. Triage surviving/uncovered mutants, map relevant cases to `CLOSE-004` and `CLOSE-005`, and establish a measured CI budget and reviewed gate. Keep explicit ordering-defect experiments where Stryker's operators cannot express the required fault. |
| F: expansion | Add deliberate scopes for reassembly/masking C# boundaries only after reviewing cost and usefulness. Continue mutating the Dafny sources for core algorithm contracts. |

Increment E should not be considered complete without a recorded Stryker run and a disposition for relevant surviving/uncovered mutants. Dispositions distinguish missing tests, equivalent mutations, out-of-scope behavior and defects; do not blanket-exclude survivors to improve the score. Establish a score threshold only after this review. Gate critical named behavioral experiments separately so an aggregate score cannot hide their failure.

Stryker runs use the production test suite and can be expensive. Use a separate worktree during concurrent work, and avoid starting a second run in the same worktree. No CI configuration is changed by this pilot; receipt integration and CI policy belong to D/E after increment B has landed.

## Initial experiment, November 7, 2026

The first run used release-branch source at `f60dd9b`, .NET SDK 10.0.403 and Stryker.NET 5.0.0 with one worker and the progress reporter. The Release build and initial run of 184 tests succeeded. Selection reported 67 runnable mutants and nine uncovered mutants. Stryker also reported 46 compile-error outcomes during mutation preparation; these are not detected behavioral defects.

The serial experiment was interrupted at the agreed 15-minute limit (exit 130). It did not produce a final mutation report or score. Its result is **incomplete**, not passed. The local `artifacts/stryker-close/pilot-serial.log` and `pilot-input-hashes.json` retain the available evidence; these ignored artifacts are not part of the committed documentation.

After this experiment, the configuration was changed to four workers and the dots reporter, and the script was updated to retain console logs. That configuration passed JSON/shell checks but has not yet completed a mutation run. Increment E must establish the completed parallel baseline and measure its duration before adding a CI gate. No production source was changed.

## References

- [Stryker.NET configuration](https://stryker-mutator.io/docs/stryker-net/configuration/): source selection, target framework, reporters and score thresholds.
- [Stryker.NET reporters](https://stryker-mutator.io/docs/stryker-net/reporters/): local HTML and JSON evidence.
- [Microsoft Research: Formally verifying agent-written full-stack web apps with PDD Kit](https://www.microsoft.com/en-us/research/blog/formally-verifying-agent-written-full-stack-web-apps-with-pdd-kit/): the phase 2 inspiration for challenging contracts and recording evidence at trusted boundaries.

## Increment E completed pilot and gate

An exploratory four-worker run on November 7, 2026 completed with exit 0 in
**458.30 seconds (7m 38s)**, using Stryker.NET 5.0.0, Release, the net10.0 library
and .NET 10 host. It ran the eight initial controlled cases plus the existing
suite. The selected connection-handler file produced these separate outcomes:

| Outcome | Count |
| --- | ---: |
| Killed by tests | 22 |
| Survived | 45 |
| No coverage | 8 |
| Timeout | 1 |
| Compile error | 3 |
| Ignored by redundant-block filter | 25 |

The console's 46 compile-error outcomes include mutation preparation outside the
selected file; the JSON contains nine compile errors across its reported files.
Neither is counted as a behavioral detection. The headline score was 30.26%; it
includes the timeout and is **not the gate**. The JSON SHA-256 was
`2fef3f868596c575038bd716a90d25ebfab288ae46fd1229b843a0c13bdc8538`.
This exploratory report lives in ignored `artifacts/stryker-close/reports/` and
is not combined with later revision-bound evidence.

The pilot exposed missing assertions for removal of the parser's
`NotifyReadFailure` (pilot mutant 590) and `NotifyPeerClose` (627). Two additional
loopback cases now hold the peer timer and assert readiness after the parser
notification. The initial report deliberately fails the strengthened gate;
these mutants must be **Killed** in the final run, as must removal of Close send
(631), peer wait (632), Disconnected notification (638), disposal await (643), and
async transport disposal (646). IDs are explanatory only: the gate keys exact
operator/location/replacement fingerprints and the source hash.

[Machine-readable dispositions](stryker-close-dispositions.json) classify every
remaining selected survivor, uncovered mutant and timeout individually and map
Close-relevant entries to CLOSE-004/CLOSE-005. Important distinctions:

- Boolean values carried by the unused termination signal, empty parser Dispose,
  and Release-elided Debug.WriteLine calls are equivalent for this scope.
- Handshake/ping setup, reserved-opcode diagnostics, Ping response timeout and
  synchronous abort cleanup are outside these two graceful-disposal obligations.
- Custom synchronization contexts, parser-only completion fallback, precise
  observer terminal routing and active periodic-ping cleanup remain **accepted
  test gaps**, not detected defects or proofs of equivalence. PR review must
  assess these dispositions independently. This does not claim all Close-related
  behavior is covered.
- The Ping-response timeout is kept as Timeout, never relabeled Killed. The
  redundant-block ignored cases remain visible and require the known Stryker
  filter reason. Unexpected ignored outcomes fail the gate.

CI runs the separate Stryker job on PRs, with a **30-minute command budget** and
**40-minute job limit**. The command budget is almost four times the measured
pilot duration, allowing slower shared workers and restore overhead; the job
limit also covers SDK setup and evidence upload. It is an initial budget to
reassess from CI measurements, not a promised runtime. The small Dafny mutation
suite and all existing gates remain mandatory.

The empirical gate requires a completed successful run, exact source/config
identity, actual host-runtime probe, distinct raw outcomes, dispositions for
survivors/uncovered/timeouts, and Killed outcomes for the seven critical mutants.
A new or unresolved outcome fails; changing source invalidates the dispositions.
An aggregate score cannot waive this gate or the independent
[ordering-defect experiment](controlled-close.md). Review acceptance remains a
separate unsatisfied obligation.

Use `./scripts/pdd.sh capture --job stryker --output artifacts/pdd/stryker` for a
fresh source-bound run. `stryker-triage --report <JSON> --output <directory>` can
check dispositions alone; it does not establish source-bound execution evidence.
The final receipt reports its own counts, duration, source identity, report hash,
SDK/tool versions, commands, exits, timestamps and host rather than substituting
this exploratory measurement. All jobs must capture one unchanged revision.

A second complete run after adding the parser notification tests took **461.41
seconds (7m 41s)**. All seven critical mutants were Killed. Selected outcomes were
23 Killed, 43 Survived, seven NoCoverage, three Timeout, three CompileError and 25
Ignored. Removing Aborted or incoming-Close status callbacks now times out the
notification barriers; those remain Timeout rather than assertion detection.
The report exposed one additional survivor: reversing the parser-finally
cancellation condition (594). Its outcome changed from Killed in the first run
to Survived, exposing nondeterministic pipeline-only coverage. It has an explicit
accepted-gap disposition alongside removal of the same finally call (596).
The aggregate correctly rejected the report until that disposition was recorded.
Final captures are refreshed after this disposition change; prior receipts are
not relabeled with the new source identity.

A subsequent run classified critical mutant 627 as Timeout instead of Killed,
and the gate rejected it. In an isolated source copy, the same removal failed the
named parser-readiness assertion in 89 ms (the companion read-failure case passed).
The test detects the defect; the Stryker timeout is not equivalent evidence.
The selected test set also includes loopback tests whose mutated peer waits can
consume five seconds each before the controlled assertion runs.

The final configuration therefore sets `additional-timeout` to **30000 ms**.
[Stryker's per-mutant timeout calculation](https://stryker-mutator.io/docs/stryker-net/configuration/#additional-timeout-number)
uses the covering tests' baseline duration, with only 3000 ms additional time by
default. Thirty seconds lets the 15-second diagnostic waits and bounded cleanup
finish before cancellation by Stryker, with margin for mutated five-second peer
waits. This increases the opportunity to observe assertions; it does not relabel
any previous Timeout or relax the requirement for all seven critical mutants to
be Killed. The overall command/job budgets remain 30/40 minutes and the final
receipt measures the completed run with this configuration.

## Increment G supersedes the gap dispositions above

The [G record](close-evidence-g.md) retains the post-F failure rather than treating
its critical Timeout as Killed. G extracts the existing parser lifecycle into an
internal task seam and injects the default-preserving periodic scheduler. Exact
old/new operator, source-snippet and replacement matches are retained in
[evidence/increment-g/fingerprint-migration.json](evidence/increment-g/fingerprint-migration.json);
all seven original critical obligations remain, now bound to the new source hash.
Eight newly detected lifecycle/routing/cleanup/context fingerprints and the
scheduler-substitution check are also required Killed. Numeric IDs are not keys.

Stryker runs with `disable-mix-mutants: true` and two workers. This separates
unrelated missing-Pong and status-notification waits from critical assertion
experiments and matches the failed CI runner's recommended worker count. No test
or mutant is excluded; the 30-second additional timeout and 30/40-minute budgets
are unchanged. Real loopback and the 200-iteration error-routing stress test stay
in Stryker and the complete framework matrix. The stress test now releases the
peer wait only in cleanup after its assertion and drains disposal before its next
iteration, reducing background teardown work under mutation.

The direct lifecycle tests distinguish cancellation, explicit terminal routing
and fallback closure; the virtual scheduler tests cover timer cleanup and a paused
periodic Ping. DisposeAsync is tested under a non-pumping caller context. Other
ConfigureAwait sites still have explicit accepted-gap dispositions. All raw
outcomes, including timeout and compile-error, remain distinct. Human review of
these dispositions is pending; this file records source-based rationale, not
revision-specific acceptance.
