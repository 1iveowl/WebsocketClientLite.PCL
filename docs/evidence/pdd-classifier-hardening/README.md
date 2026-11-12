# Evidence: Pdd classifier and fixture hardening (S10, 2026-11-11 and 2026-11-12)

Step S10 of `docs/pdd-10.3-session-plan.md`: findings 4 to 7 of the #136 code review and
the CI mutant timing. Base `29697a4` (`origin/releases/10.3.0.0`). The change is
`cad2325`. Work started on 2026-11-11 and the last local checks and CI run finished on
2026-11-12 (UTC). Each file below carries its own time.

**Load and the timing lock.** Other step sessions ran on the same machine during the
whole step. Every timed run whose seconds are reported here (Pdd test runs, the mutation
suites, the capture jobs) ran under the shared timing lock
`flock ~/worktrees/.timing.lock`, so no other session's timed run overlapped it. Next to
each such log, `<name>.ps.txt` has the time the lock was taken, the output of
`ps -eo pid,etime,comm | grep -E 'claude|dotnet' | grep -v grep` just before the run, and
the time the lock was released. It holds process names only. Builds and untimed checks
(`pdd.sh validate`, `dafny-masking.sh --check`, `aggregate`) did not take the lock. All
times are local measurements under that load.

## Commands

`scripts/` has every command used:

- `timed.sh <prefix> <command>`: takes the lock, writes `<prefix>.ps.txt`, runs the
  command into `<prefix>.log` and appends its exit code and elapsed time.
- `shift-closestep.sh <commit> shift|noshift`: copies the source with `git archive` into a
  new directory, makes the copy its own git repository, adds one comment line directly
  above `CloseStep`'s comment block in `verification/Protocol.dfy` (or adds nothing for
  `noshift`), and runs the Pdd tests there.
- `experiment.py <repo> <commit|worktree> [--add F] [--replace F OLD NEW] [--filter X]`:
  the same kind of isolated copy, with files added from the working tree or single
  edits, then the Pdd tests (optionally filtered).
- `after-checks.sh`, `after-stryker.sh`: the checks on `cad2325`, see "After".
- `compare-times.py`: the per-mutant time tables below.

The working checkout was never edited by an experiment.

## Baseline, `29697a4`, clean tree (2026-11-11)

`baseline/`:

- `pdd-tests.*`: Pdd tests, run and passed, 262 of 262.
- `mutations/`, `mutations.*`, `mutations-outcomes.txt`: full mutation suite, run and
  passed. Baseline passed, 17 of 17 detected, each in 8.8 to 9.9 s, and each at the
  matched clauses listed. No mutant is above 30 s, a third of its 90 s budget.
- `finding-7-shift-closestep-attempt-1-no-git.*`: attempt 1 of the line-shift run, kept.
  Run and failed, 12 of 262. One of the 12, `ReceiptTests.CaptureFailureSkipsFollowingAndRejectsStaleDirectory`,
  failed only because that copy was not a git repository ("fatal: not a git
  repository"). The script then got its `git init` step; the kept `shift-closestep.sh` is
  that second version.
- `finding-7-control-noshift.*`: the unchanged copy, as a git repository: run and passed,
  262 of 262.
- `finding-7-shift-closestep.*`: one comment line above `CloseStep`: run and failed, 11 of
  262. The failures are `MutationTests.PythonDiagnosticFixturesMatch` (target case),
  `MutationTests.SuiteIsolationAndPartialEvidence(success)`, `PayloadEvidenceTests.PayloadSuiteEditsOneTemporarySourceAtATime`
  and eight `PayloadEvidenceTests.ObservedDiagnosticsRequireTheExactSemanticFailure` rows:
  every `Protocol.dfy` fixture line below the insertion. So `payload-diagnostics.json` pinned lines too,
  not only the two files the review named.

`ci-43115756212/`: CI dispatched on the branch at `29697a4` before any change
(`gh workflow run ci.yml --ref chore/pdd-classifier-hardening`). Every job passed
(`run.json`); `qualify-host` and `contract-change-report` were skipped as on every manual
run. From the `pdd-protocol` artifact: `mutations-results.json`, `mutations.log`,
`protocol-job.json`, and `per-mutant-times.txt`. All 17 mutants detected in 17.2 to 17.8 s.

`ci-43046774633/`: the same files from the #136 run on `34a8950`, downloaded on
2026-11-11 for the comparison. The run `43041451305` on `9673862` is already kept in
`docs/evidence/header-review-fixes/ci-43041451305/`.

## Fixture anchors (finding 7)

`fixture-anchors/`: `anchor-fixtures.py` rewrote every Dafny line in
`verification/tests/fixtures/mutation-cases.json` and `payload-diagnostics.json` as
`{Module.Class.Member+N}`, relative to the line of the enclosing declaration's body
brace, in the locations and in the source excerpts of the observed output.
`convert-output.txt`: rendered against the source at `29697a4`, both files are equal to
what they were before, and no absolute Dafny location is left. The anchor table it used
is listed there. `tools/Pdd.Tests/Fixtures.cs` renders the anchors with
`Requirements.DafnyMember`, and the tests passing on `29697a4`'s `Protocol.dfy` show that
the C# lookup finds the same lines.

## Failing first (findings 4 to 6)

`failing-first/`:

- `findings-4-5-baseline.*`: `29697a4` plus only the new
  `tools/Pdd.Tests/ClassifierHardeningTests.cs`, which uses only public APIs that exist at
  the base. Run and failed, 9 of 10. The one that passes is the control
  (`UneditedSourceIsDetected`). Each failure is the defect named:
  - bodyless named function: `detected`, expected `inconclusive` (it took
    `HeaderValid`'s body);
  - set display in a `requires` or `ensures` of the named predicate: `inconclusive`,
    expected `detected` (it took `{0, limit}` or `{length}` as the body), two rows;
  - unbalanced body: `InvalidDataException: unbalanced declaration body` out of
    `Classify`; duplicate named predicate: `detected`, expected `inconclusive`;
  - `function {:opaque} HeaderValid(` and `{:opaque} {:fuel 2}`: `inconclusive`, expected
    `detected` (the pattern allowed no attribute), two rows;
  - a `HeaderValid` in another class and in another module: `detected`, expected
    `inconclusive`, two rows.
- Finding 6 is a gap in a test, not in the classifier, so it is shown on copies with the
  check the test should guard removed:
  - `finding-6-baseline-name-check-removed.*`: `29697a4` with the line that limits the
    lookup to predicates the target clause names deleted. The old test: run and passed,
    1 of 1. It does not notice.
  - `finding-6-changed-name-check-removed.*`: the changed tree with the same check
    removed. The new theory: run and failed, 2 of 4. The `Known` and `PrefixValid` rows
    report `detected`, expected `inconclusive`.
  - `finding-6-baseline-declaration-reformatted.*`: `29697a4` with
    `static method DecodeHeader(` written with two spaces. The old test: run and passed,
    1 of 1, although its line lookup found nothing and it moved the location to line 0.
  - `finding-6-changed-declaration-reformatted.*`: the same edit on the changed tree. The
    new theory: run and failed, 1 of 4: `line not found: static method DecodeHeader(`.

## After, `cad2325`, clean tree (2026-11-11 and 2026-11-12)

`after/`, from `after-checks.sh` and `after-stryker.sh`:

- `finding-7-shift-closestep.*`: one comment line above `CloseStep`: run and passed, 275 of
  275.
- `findings-4-5-changed.*`: the new classifier tests and the proposition theory: run and
  passed, 14 of 14.
- `validate.log`: run and passed, 16 requirements.
- `dafny-masking-check.log`: run and passed, `Dafny program verifier finished with 41 verified, 0 errors`.
- `mutations/`, `mutations.*`: full mutation suite, run and passed. 17 of 17 detected in
  8.5 to 9.5 s. Every experiment has the same status and the same matched clauses as the
  baseline run (compared on `results.json`).
- `capture-*.log`, `capture-*.ps.txt`, `aggregate.log`, and the capture set in
  `capture-cad2325.zip`: see the checks table.

`after-checks.sh` was stopped by the session's two-hour limit for a background command
while its Stryker capture was still waiting for the timing lock, before that capture
started. Its protocol, tests and controlled captures had finished. `after-stryker.sh`
ran the Stryker capture and `aggregate` on the same commit and the same capture directory.

## Checks on `cad2325`

| Check | Result |
| --- | --- |
| Pdd tests | run and passed: 275 of 275 (protocol capture `fixtures`, and the line-shift copy). 262 at the base, plus 10 in `ClassifierHardeningTests`, plus 3 because the proposition test became a 4-row theory |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| `scripts/dafny-masking.sh --check` | run and passed: `Dafny program verifier finished with 41 verified, 0 errors` |
| Mutation suite | run and passed: baseline passed, 17 of 17 detected, classifications and matched clauses identical to the base run |
| Five-target matrix, Release | run and passed: 307 of 307 on each of netstandard2.0, netstandard2.1, net8.0, net9.0 and net10.0 (all .NET 10.0.14 hosts), in the tests capture |
| Coverage gate | run and passed: line coverage 83.1 % (70 % required), in the tests capture |
| `capture --job protocol` | run and passed: manifest, fixtures, proof, mutations (17 of 17 detected) |
| `capture --job tests` | run and passed: restore, build, matrix, coverage run, coverage gate |
| `capture --job controlled` | run and passed: defect, boundaries, queued, leases, retention, reconnect |
| `capture --job stryker` | run and passed: runtime and recorded dispositions matched; `verification/stryker-close-dispositions.json` unchanged. No production or test code changed in this step |
| `aggregate` | run and passed: machine evidence gate passed; review evidence not-run |

## Per-mutant times, local

From `compare-times.py` on the three local `results.json` files. All three ran under the
lock.

| Experiment | local suite, 29697a4 | local suite, cad2325 | protocol capture, cad2325 |
| --- | --- | --- | --- |
| `baseline` | 9.1 s, passed | 9.1 s, passed | 59.4 s, passed |
| `CLOSE-001-duplicate-send` | 9.0 s, detected | 8.5 s, detected | 8.8 s, detected |
| `CLOSE-001-suppressed-send` | 8.8 s, detected | 8.7 s, detected | 8.9 s, detected |
| `CLOSE-002-state-bit-loss` | 8.9 s, detected | 8.9 s, detected | 9.0 s, detected |
| `CLOSE-003-premature-release` | 9.0 s, detected | 9.0 s, detected | 8.8 s, detected |
| `CLOSE-003-suppressed-release` | 9.9 s, detected | 9.5 s, detected | 8.9 s, detected |
| `REASSEMBLY-001-offset-stall` | 9.8 s, detected | 9.0 s, detected | 8.7 s, detected |
| `REASSEMBLY-002-offset-stall` | 9.3 s, detected | 8.8 s, detected | 8.8 s, detected |
| `REASSEMBLY-001-oversized-result` | 9.2 s, detected | 9.3 s, detected | 9.2 s, detected |
| `MASK-001-xor-to-or` | 8.8 s, detected | 8.9 s, detected | 8.7 s, detected |
| `MASK-001-key-write` | 9.0 s, detected | 9.0 s, detected | 9.0 s, detected |
| `MASK-002-tail-xor-to-or` | 8.8 s, detected | 8.9 s, detected | 8.7 s, detected |
| `MASK-002-corrupt-bulk-byte` | 8.8 s, detected | 9.5 s, detected | 9.4 s, detected |
| `MASK-002-preserve-prefix` | 9.4 s, detected | 8.7 s, detected | 8.8 s, detected |
| `MASK-003-xor-to-or` | 9.0 s, detected | 9.1 s, detected | 8.5 s, detected |
| `MASK-003-preserve-prefix` | 8.8 s, detected | 8.9 s, detected | 8.9 s, detected |
| `HEADER-001-short-sixteen-bit` | 8.8 s, detected | 8.5 s, detected | 9.0 s, detected |
| `HEADER-001-short-sixty-four-bit` | 9.0 s, detected | 8.9 s, detected | 9.3 s, detected |

No mutant is above 30 s, a third of its 90 s budget, in any local run. One outlier: the
**baseline** verification of the protocol capture took 59.4 s, a third of its 180 s
budget, against 9.1 s in both standalone suites and 8.8 s and 8.9 s in the protocol
captures of #136 (`911f2d4`, `34c2421`). It passed. The process list taken when the lock
was acquired shows no other `dotnet` process, but the lock serializes only timed runs, and
other sessions' builds and untimed checks may have run during it. The cause is not
established. It was not rerun.

## Per-mutant times, CI

Hosted runners, `verify-protocol`, from each run's `pdd-protocol` artifact. 43041451305
is kept in `docs/evidence/header-review-fixes/`, 43062905406 (S4, on the test parity
branch) in `docs/evidence/header-test-parity/part-2/checks/`, the other two here.

| Experiment | 43041451305 (9673862) | 43046774633 (34a8950) | 43062905406 (S4) | 43115756212 (29697a4) |
| --- | --- | --- | --- | --- |
| `baseline` | 21.6 s, passed | 21.1 s, passed | 10.9 s, passed | 17.9 s, passed |
| `CLOSE-001-duplicate-send` | 21.1 s, detected | 20.9 s, detected | 11.2 s, detected | 17.7 s, detected |
| `CLOSE-001-suppressed-send` | 21.6 s, detected | 20.4 s, detected | 10.8 s, detected | 17.6 s, detected |
| `CLOSE-002-state-bit-loss` | 21.5 s, detected | 21.0 s, detected | 11.0 s, detected | 17.5 s, detected |
| `CLOSE-003-premature-release` | 90.1 s, inconclusive, timed out | 21.0 s, detected | 10.5 s, detected | 17.8 s, detected |
| `CLOSE-003-suppressed-release` | not-run | 21.4 s, detected | 10.9 s, detected | 17.6 s, detected |
| `REASSEMBLY-001-offset-stall` | not-run | 21.3 s, detected | 10.8 s, detected | 17.6 s, detected |
| `REASSEMBLY-002-offset-stall` | not-run | 21.6 s, detected | 10.5 s, detected | 17.5 s, detected |
| `REASSEMBLY-001-oversized-result` | not-run | 21.3 s, detected | 10.9 s, detected | 17.6 s, detected |
| `MASK-001-xor-to-or` | not-run | 21.2 s, detected | 11.2 s, detected | 17.2 s, detected |
| `MASK-001-key-write` | not-run | 20.9 s, detected | 10.9 s, detected | 17.6 s, detected |
| `MASK-002-tail-xor-to-or` | not-run | 21.5 s, detected | 11.1 s, detected | 17.4 s, detected |
| `MASK-002-corrupt-bulk-byte` | not-run | 21.0 s, detected | 11.5 s, detected | 17.8 s, detected |
| `MASK-002-preserve-prefix` | not-run | 21.6 s, detected | 11.1 s, detected | 17.6 s, detected |
| `MASK-003-xor-to-or` | not-run | 20.9 s, detected | 11.1 s, detected | 17.5 s, detected |
| `MASK-003-preserve-prefix` | not-run | 21.4 s, detected | 11.3 s, detected | 17.3 s, detected |
| `HEADER-001-short-sixteen-bit` | not-run | 21.6 s, detected | 11.0 s, detected | 17.5 s, detected |
| `HEADER-001-short-sixty-four-bit` | not-run | 21.2 s, detected | 10.8 s, detected | 17.7 s, detected |

43041451305 (9673862): mutants with a time 4, min 21.1 s, max 90.1 s, over 30 s (a third of the 90 s budget): 1
43046774633 (34a8950): mutants with a time 17, min 20.4 s, max 21.6 s, over 30 s (a third of the 90 s budget): 0
43062905406 (S4): mutants with a time 17, min 10.5 s, max 11.5 s, over 30 s (a third of the 90 s budget): 0
43115756212 (29697a4): mutants with a time 17, min 17.2 s, max 17.8 s, over 30 s (a third of the 90 s budget): 0

The baseline CI run of this step, 43115756212 on `29697a4`, passed with every mutant at
17.2 to 17.8 s, about twice the local times. Across the four runs the same catalog took
about 10.5 to 11.5 s (43062905406), 17.2 to 17.8 s (43115756212) and 20.4 to 21.6 s
(43046774633) per mutant, and one mutant hit 90 s once (43041451305). The hosted runner's
speed varies by about a factor of two between runs, and the 90 s outlier is still the
only one seen. One more passing run does not show that it is gone, and its cause is still
not established. Budgets and `--cores` are unchanged.

### The run on the final commit (added 2026-11-12)

`ci-43137975656/`: CI dispatched on `371826a`, the evidence commit on top of `cad2325`
(`gh workflow run ci.yml --ref chore/pdd-classifier-hardening`). Every job passed
(`run.json`); `qualify-host` and `contract-change-report` were skipped. The same files
as for the baseline run. All 17 mutants detected in 10.2 to 11.3 s, with the same
status and matched clauses as in 43115756212. This folder is added in the commit after
`371826a` and contains only these files.

| Experiment | 43041451305 (9673862) | 43046774633 (34a8950) | 43062905406 (S4) | 43115756212 (29697a4) | 43137975656 (371826a) |
| --- | --- | --- | --- | --- | --- |
| `baseline` | 21.6 s, passed | 21.1 s, passed | 10.9 s, passed | 17.9 s, passed | 10.8 s, passed |
| `CLOSE-001-duplicate-send` | 21.1 s, detected | 20.9 s, detected | 11.2 s, detected | 17.7 s, detected | 11.2 s, detected |
| `CLOSE-001-suppressed-send` | 21.6 s, detected | 20.4 s, detected | 10.8 s, detected | 17.6 s, detected | 10.9 s, detected |
| `CLOSE-002-state-bit-loss` | 21.5 s, detected | 21.0 s, detected | 11.0 s, detected | 17.5 s, detected | 11.0 s, detected |
| `CLOSE-003-premature-release` | 90.1 s, inconclusive, timed out | 21.0 s, detected | 10.5 s, detected | 17.8 s, detected | 11.3 s, detected |
| `CLOSE-003-suppressed-release` | not-run | 21.4 s, detected | 10.9 s, detected | 17.6 s, detected | 10.6 s, detected |
| `REASSEMBLY-001-offset-stall` | not-run | 21.3 s, detected | 10.8 s, detected | 17.6 s, detected | 10.7 s, detected |
| `REASSEMBLY-002-offset-stall` | not-run | 21.6 s, detected | 10.5 s, detected | 17.5 s, detected | 10.7 s, detected |
| `REASSEMBLY-001-oversized-result` | not-run | 21.3 s, detected | 10.9 s, detected | 17.6 s, detected | 10.8 s, detected |
| `MASK-001-xor-to-or` | not-run | 21.2 s, detected | 11.2 s, detected | 17.2 s, detected | 10.7 s, detected |
| `MASK-001-key-write` | not-run | 20.9 s, detected | 10.9 s, detected | 17.6 s, detected | 10.2 s, detected |
| `MASK-002-tail-xor-to-or` | not-run | 21.5 s, detected | 11.1 s, detected | 17.4 s, detected | 10.7 s, detected |
| `MASK-002-corrupt-bulk-byte` | not-run | 21.0 s, detected | 11.5 s, detected | 17.8 s, detected | 10.8 s, detected |
| `MASK-002-preserve-prefix` | not-run | 21.6 s, detected | 11.1 s, detected | 17.6 s, detected | 10.8 s, detected |
| `MASK-003-xor-to-or` | not-run | 20.9 s, detected | 11.1 s, detected | 17.5 s, detected | 10.8 s, detected |
| `MASK-003-preserve-prefix` | not-run | 21.4 s, detected | 11.3 s, detected | 17.3 s, detected | 10.8 s, detected |
| `HEADER-001-short-sixteen-bit` | not-run | 21.6 s, detected | 11.0 s, detected | 17.5 s, detected | 10.4 s, detected |
| `HEADER-001-short-sixty-four-bit` | not-run | 21.2 s, detected | 10.8 s, detected | 17.7 s, detected | 10.7 s, detected |

43041451305 (9673862): mutants with a time 4, min 21.1 s, max 90.1 s, over 30 s (a third of the 90 s budget): 1
43046774633 (34a8950): mutants with a time 17, min 20.4 s, max 21.6 s, over 30 s (a third of the 90 s budget): 0
43062905406 (S4): mutants with a time 17, min 10.5 s, max 11.5 s, over 30 s (a third of the 90 s budget): 0
43115756212 (29697a4): mutants with a time 17, min 17.2 s, max 17.8 s, over 30 s (a third of the 90 s budget): 0
43137975656 (371826a): mutants with a time 17, min 10.2 s, max 11.3 s, over 30 s (a third of the 90 s budget): 0

So the same catalog took 17.2 to 17.8 s per mutant on the base and 10.2 to 11.3 s after the
change, on two hosted runs a few hours apart. The change touches only the C# classifier
and fixtures, not what Dafny verifies, so the difference is the runner's, not the change's.
Still no mutant above a third of its budget, and still no explanation for the one 90 s
timeout.
