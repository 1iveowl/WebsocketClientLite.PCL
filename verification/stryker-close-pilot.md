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
