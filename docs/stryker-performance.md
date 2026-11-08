# Stryker performance experiments

On this 32-logical-CPU development host, eight Stryker workers reduced the median full Close mutation run from a single fresh two-worker control of **12m 35s** to **5m 41s** (55% less elapsed time). Three eight-worker runs passed the existing critical-mutant gate with zero timeouts. Local runs now default to eight workers. GitHub Actions explicitly selects two because the measurements qualify this development host, not the smaller hosted runners.

P3-E was completed and merged through [PR 119](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/119) before these experiments. The measurements below precede the implementation. The branch now adds an explicit captured concurrency override and forces the test project to copy its resolved library DLL, preventing equal-size/equal-timestamp reuse. Local default concurrency is eight; hosted CI explicitly retains two. Test assertions, mutation scope and acceptance gates remain unchanged.

## Measurements

All trials used clean commit `52343c2134f27b2fb8bda4290f0c770271c42712` in a separate topic worktree. They ran sequentially, without competing benchmark jobs, using Stryker.NET 5.0.0, .NET 10.0.14, Ubuntu 24.04.5 LTS x64, and an AMD Ryzen 9 9955HX (16 cores / 32 logical CPUs). No container CPU or memory quota was set. The archived runtime TRX identifies the actual loaded net10.0 library, including path, MVID and SHA-256; `LibTfm` is not the host runtime.

| Trial | Workers | Stryker command seconds | Selected outcomes vs accepted E | Critical gate |
| --- | ---: | ---: | --- | --- |
| Control | 2 | 755.487 | One existing out-of-scope variation | Pass, zero timeouts |
| Screening | 4 | 500.778 | Identical | Pass, zero timeouts |
| Candidate 1 | 8 | 366.755 | Identical | Pass, zero timeouts |
| Candidate 2 | 8 | 341.125 | Identical | Pass, zero timeouts |
| Candidate 3 | 8 | 339.096 | Same out-of-scope variation as control | Pass, zero timeouts |

The six Stryker commands consumed 2,423.479 seconds (40.39 local machine minutes) in total, including the control discrepancy and rejected changed-file probe. This excludes runtime probes, analysis and subsequent repository checks; no hosted-runner jobs were launched for these trials, and monetary cost was not measured.

Eight-worker median: 341.125 seconds; range: 339.096–366.755 seconds. Four workers saved 33.7% against the control; eight-worker median saved 54.8%. These are observations, not a timing SLA. The two- and four-worker settings have one measurement each; trial order was not randomized, caches were warm after the initial build, and background host load was not controlled. Each trial used a new process and output directory, not reused mutation results.

Each full trial discovered 269 tests, generated 943 mutants before filtering, and executed the same 69 mutants. Existing per-test coverage selection was already enabled. Mutant mixing stayed disabled, the mutation scope stayed unchanged, and the additional timeout stayed at 30 seconds. All 16 required fingerprints were killed on every full run.

The original control's **exact-outcome comparison failed** even though repository triage passed. Preserve that failure in `c2-r1/trial.json`; the subsequent `control-review.json` records the decision to continue. Only fingerprint `b0b3581ae9b5edc715f27733b23dd0b219cd949f7a59407630aaf6c7d39c864a` was permitted to vary between Killed and Survived. It removes `throw handshakeException;` at line 95, is already classified out-of-scope in the disposition catalog, and was previously killed by `ReconnectTests.FailedConnectTerminatesAndRemovesRegistration`. No new disposition or exception to the critical gate was introduced. Every other selected fingerprint had to match the accepted E report, and zero timeouts were required. The last eight-worker repeat reproduced this same variation; it is not an identical-outcome run.

## Using the measured local setting

The existing script forwards Stryker options:

```bash
PDD_STRYKER_OUTPUT="$PWD/artifacts/stryker-local-8-unique" \
  ./scripts/stryker-close.sh --concurrency 8
```

Use a fresh output directory and avoid competing builds/tests while measuring. This is a raw local run, not by itself a complete PDD receipt. The benchmark driver archived below uses the existing `Evidence.Capture` seam and records the actual command override in `job.json`; it does not conceal an environment-only setting. Use the supported receipt capture for local acceptance:

```bash
./scripts/pdd.sh capture --job stryker --output artifacts/pdd/stryker
```

The ordinary local capture and direct script default to eight workers through the source-hashed Stryker configuration. The optional `--stryker-concurrency` override accepts positive integer worker counts only for the Stryker job and records the exact command in the receipt. GitHub Actions explicitly passes `--stryker-concurrency 2`.

## Changed-file experiment: not qualified for linked worktrees

The pinned tool advertises `--since[:<committish>]` for changed-file selection, and experimental `--with-baseline` for result reuse. Current [Stryker configuration documentation](https://stryker-mutator.io/docs/stryker-net/configuration/) describes these controls, but the archived pinned CLI help is the authority for this installation.

The `--concurrency 8 --since:HEAD` probe took **120.238 seconds** and executed **zero mutants**. The source worktree was clean against HEAD. However, Stryker reported 96 changed files with paths rooted at `/workspaces/WebsocketClientLite.PCL`, the original checkout, rather than `/tmp/ws-stryker-performance`. That list even included the original checkout's untracked `core.12592`. This is a changed-path/worktree mismatch, not evidence that changed-file selection is correct. No file in the original checkout was modified by this experiment.

The raw Stryker command exited zero, but existing triage rejected its report with **“Unexpected ignored selected mutant”**. Preserve both outcomes. Do not recommend this two-minute result as a valid shortcut in this worktree setup, and never substitute partial/reused results for fresh full acceptance. A follow-up must use an isolated clone and a nonempty changed-source fixture to verify file selection before claiming any saving. No baseline/dashboard reuse or test-case-filter setting was qualified.

## Where the time goes and remaining work

Initial tests, mutation generation and coverage collection cost roughly two minutes before mutant execution at every worker setting. More workers overlap the remaining workload but do not remove this setup cost.

The prior E TRX profile shows ordinary integration/reconnect tests taking about three seconds and two graceful-teardown tests about five seconds. Three reconnect cases cover 49 mutants each, so cleanup waits recur across mutation executions. `PayloadLeaseTests.SendObservationTimeoutRetainsPendingLease` deliberately tests the real five-second observation timeout; shortening or dropping it would weaken the evidence.

A separate follow-up can investigate peer Close echoes and incidental cleanup waits in ordinary loopback fixtures while preserving the dedicated bounded-disposal/timeout tests. Such a change needs its own behavioral and full mutation validation. It was not made or measured here. Hosted-runner concurrency qualification, higher than eight workers, mutant mixing, baseline reuse, changed-source selection in a canonical clone, and broader mutation scope remain deferred.

## Additional validation finding: stale library copy

The first full aggregate rejected the `netstandard2.1` matrix cell: its runtime evidence reported the loaded library as `.NETStandard,Version=v2.0`. All ordinary test commands had exited successfully, demonstrating why the loaded-assembly check matters. The two library outputs had the same size (136,704 bytes) and exactly the same modification timestamp (`2026-11-08 11:13:56.681510210 UTC`); this is consistent with incremental copying treating the existing DLL as unchanged.

The original failed receipt remains in the benchmark archive. The explicit validation retry clears only this worktree's test-project `bin` and `obj` directories before each library target, through an archived external matrix driver recorded in the command receipt. It preserves all tests and expected library identities. This is a validation workaround, not a checked-in fix or a claim that the ordinary warm-output matrix passed. The implementation now forces a copy of the resolved library DLL after the normal test-project build, irrespective of matching file size and timestamp. The original workaround and failure are retained as historical evidence.

## Validation and evidence

The full aggregate passed on the measured clean source after the explicitly recorded fresh-test-output matrix workaround: 269 tests per library target with actual assembly identities checked, 237 tooling tests, the Release build and 83.08% line coverage (70% gate), pinned Dafny proof/trust/generated-code checks, all 15 Dafny mutations, every controlled defect check, and the third eight-worker Stryker result. The original standard-matrix aggregate remains failed and archived. Human review remains separate and not-run; no review acceptance is inferred.

The archives retain all original trial records, command receipts/logs, mutation JSON reports, runtime TRX, host inventory, comparison driver and the source identity. They include the failed exact control comparison and rejected changed-file probe. The documentation commit is later than the measured commit; these records are not relabeled as execution against the documentation commit.

[Archive checksums and contents](evidence/stryker-performance/archives.json).

## Implemented changes and merge validation

Commit `7e002a12f0f0ef9eba87da3b71dfa5c28974c94d` adds `capture --job stryker --stryker-concurrency 8` and forces the test project to copy the resolved library DLL even when size and timestamp match an existing DLL. At that implementation commit the default remained two. The subsequent local-default change sets eight in the shared configuration and explicitly preserves two in GitHub Actions. No changed-file filtering or baseline reuse is enabled.

The fresh full aggregate passed using the supported override and the ordinary matrix, with no cleanup workaround: 244 tooling tests, 269 tests per library target with correct loaded identities, 83.08% line coverage, all proof/trust/generated-code checks, 15 Dafny mutations, and all controlled defect checks. Stryker took 286.931 seconds (4m 47s); all 105 selected outcomes matched E, all 16 critical fingerprints were killed, and there were zero timeouts. This is one implementation validation run, not a replacement for the three-run qualification range above. The equal-size/equal-timestamp wrong-DLL fixture also passed on this commit.

`implementation.zip` retains the receipt, command logs, comparison, and fixture. Earlier failed reports remain archived unchanged. The final archive/documentation commit follows this tested implementation commit; no execution is attributed to the later documentation-only commit.
