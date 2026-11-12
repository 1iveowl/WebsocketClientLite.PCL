# Evidence: release readiness of `releases/10.3.0.0` (S9, 2026-11-12)

Step S9 of `docs/pdd-10.3-session-plan.md`. Every check below ran on the tip of
`origin/releases/10.3.0.0`, commit `38a5405e80aaf39dd1dc5c17cc517c14bdc9e520` (#145), in a
clean checkout: `git status` empty before the first check and after the last, and
`dirty_worktree: false` in every capture record. The work started at 2026-11-12 03:24 UTC and
the last local check finished at 03:42 UTC. Nothing was repeated; no check failed or was
interrupted.

## Pull requests since `30cf44a`

`30cf44a` is the commit of `main` that `releases/10.3.0.0` was made from (2026-11-09).
`gh pr list --base releases/10.3.0.0 --state all` lists ten pull requests. All ten are merged
(squash), none was closed without merging.

| PR | Title | Merged (UTC) | Merge commit | Step |
| --- | --- | --- | --- | --- |
| [#136](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/136) | chore: trace the header length rule to RFC 6455 and prove its test fails first | 2026-11-11 16:10 | `d238c9e` | S1, S2 |
| [#137](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/137) | docs: plan the 10.3 traceability, test parity and boundary experiment sessions | 2026-11-11 13:39 | `bc87f6b` | plan |
| [#138](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/138) | docs: add model, effort and optional review to the 10.3 session plan | 2026-11-11 13:46 | `5a1a07a` | plan |
| [#139](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/139) | docs: add S10 for Pdd hardening and the lessons of S1, S2 and the #136 review | 2026-11-11 16:10 | `fe0bd48` | plan |
| [#140](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/140) | fix: header tests that fail without their check, header-only reads, limit boundary and truncation | 2026-11-11 22:47 | `29697a4` | S3, S4, M2 |
| [#141](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/141) | docs: add S11 for a rejected-handshake test that does not race, and lesson 10 | 2026-11-11 21:45 | `fa96d59` | plan |
| [#142](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/142) | chore: Experiment 1, the reconnect lifecycle decision as a pure function, not adopted | 2026-11-11 23:59 | `58c6ce3` | S5, S6, M3 |
| [#143](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/143) | chore: Experiment 2: typed adapter at the verified core boundary, after numbers, recommendation | 2026-11-12 01:55 | `a127b90` | S7, S8, M4 |
| [#144](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/144) | fix: rejected-handshake test that kills the line 95 mutant without a race | 2026-11-12 02:43 | `a3b8726` | S11, M7 |
| [#145](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/145) | chore: harden the Pdd proposition classifier and anchor fixture lines to declarations | 2026-11-12 03:23 | `38a5405` | S10, M6 |

Closed without merging: none. `git log --first-parent 30cf44a..origin/releases/10.3.0.0` shows
the same ten commits.

Open when this evidence was made, not in the release:
[#146](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/146) (docs: the lessons of the
overnight chain, into `releases/10.3.0.0`, opened at 03:24 UTC, as the checks started). It changes
`docs/pdd-10.3-session-plan.md` only. [#135](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/135) is a dependency bump
into `main` and does not belong to this release.

What the release changes in shipped code (`git diff 30cf44a..38a5405 -- src`, excluding the test
project): five files, all internal. `Helper/CoreBoundaryTypes.cs` (new, #143), call sites in
`DataframeParsing.cs`, `WebsocketParserHandler.cs` and `WebsocketSenderHandler.cs` (#143), and the
internal constant `TcpConnectionService.AbortedUnexpectedlyMessage` (#136). `verification/Protocol.dfy`
gains 105 comment lines (#136) and the generated `Masking.g.cs` is unchanged. `GitVersion.yml` and the
project files are unchanged.

## Checks on `38a5405`

Timed commands ran under the shared timing lock (`flock ~/worktrees/.timing.lock`, through
`scripts/timed.sh`). Next to each timed log, `<name>.ps.txt` has the time the lock was taken, the
output of `ps -eo pid,etime,comm | grep -E 'claude|dotnet' | grep -v grep` taken just before the run
(process names only), and the time the lock was released. The controller's note said no other step
session ran. Each list shows three `claude` processes and a group of `dotnet` processes that started
with this session's own build (build servers, left running between commands). Builds and untimed checks did not take the lock. All times are local
measurements. `scripts/release-checks.sh` ran every command in order; its output is `driver.log`.
Kept evidence was written outside the worktree first, so that the tree stayed clean for the captures.

| Check | Result | Log |
| --- | --- | --- |
| Release build, `dotnet build src/main/WebsocketClientLite.sln -c Release` | run and passed, 0 warnings, 0 errors (incremental: the worktree's own `obj` directories were warm, the matrix below rebuilds each target) | `build.log` |
| Five-target matrix, `./test-all-tfms.sh Release` | run and passed: 328 of 328 on each of netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 (all run on the .NET 10 host), 132.0 s | `test-all-tfms.log` |
| Coverage run, `dotnet test ... --collect:"XPlat Code Coverage"` | run and passed: 328 of 328, 27.2 s | `coverage.log` |
| Coverage gate, `scripts/check-coverage.sh ... 70` | run and passed: 84.3 % line coverage (70 % required), 79.3 % branch | `coverage-gate.log` |
| Pdd tests, `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release` | run and passed: 289 of 289, 14.9 s | `pdd-tests.log` |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements | `validate.log` |
| `./scripts/dafny-masking.sh --check` | run and passed: `Dafny program verifier finished with 41 verified, 0 errors`, generated code identical | `dafny-masking-check.log` |
| Mutation suite, `./scripts/pdd.sh mutations` | run and passed: baseline passed, 17 of 17 detected, each in 9.1 to 10.1 s (baseline 10.7 s); none above 30 s, a third of its 90 s budget. Total 175.8 s | `mutations.log`, `mutations/results.json` |
| `capture --job protocol` | run and passed (218.7 s): `41 verified, 0 errors`, mutations passed | `capture-protocol.log` |
| `capture --job tests` | run and passed (164.0 s): restore, build, matrix, coverage, coverage gate | `capture-tests.log` |
| `capture --job controlled` | run and passed (66.7 s): defect, boundaries, queued, leases, retention, reconnect | `capture-controlled.log` |
| `capture --job stryker` | run and passed (286.7 s): killed 42, survived 27, no coverage 7, compile error 3 of the selected 79; every recorded disposition matched, `stryker-close-dispositions.json` unchanged | `capture-stryker.log` |
| `aggregate` | run and passed: machine evidence gate **passed**; review evidence not-run | `aggregate.log`, `capture-summary.md` |

Capture directories: `capture-38a5405.zip` holds `pdd-s9/` (the four jobs, `receipt.json`,
`summary.md`), `s9-mutations/` (the mutation suite) and `s9-coverage/`.

Review evidence is not-run, as in every earlier receipt: no requirement is claimed satisfied by
review. `HEADER-001` and `HEADER-002` review obligations, and `CLOSE-004` and `CLOSE-005`, still need
a revision-specific human review.

### Comparison with the steps' own numbers

Test counts rise as steps add tests. Per target: 269 on #136's branch, 307 on #140's, 286 on #143's
and 311 on #144's (each before the others merged), 328 on the merged tip. The Pdd tests went from 262 to 275 (#145) and to 289 (#145's
review fixes). Mutants: 17 of 17 at every point; local times 8.5 to 10.1 s in every run. Line coverage
83.1 % to 84.3 %. The Stryker line 95 mutant (`throw handshakeException;`, lesson 10) is killed
here, and `killedBy` lists `HandshakeRejectionTests.RejectedHandshakeThrowsAndNeverInstallsSender`
four times and the racing `ReconnectTests.FailedConnectTerminatesAndRemovesRegistration` once.

### Things to know when reading the numbers

- The matrix runs every library target against a .NET 10 test host. It is not five runtimes.
- The mutation suite and the protocol capture both ran the 17 catalogued Dafny mutants. They are the
  same catalog on the same source and agree.
- The Stryker run mutates `WebsocketConnectionHandler.cs` only (the Close and handshake scope), as in
  every earlier receipt. It is not a mutation score for the library.
- No benchmark was run. S9 has no performance claim; the benchmark numbers belong to #143.

## CI on the release tip

`gh workflow run ci.yml --ref releases/10.3.0.0` (lesson 2), dispatched 2026-11-12 03:43 UTC on
`38a5405`, run [43150094060](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/43150094060),
finished 03:55 UTC. Conclusion **success**: `scope`, `verify-protocol`, `build-and-test`,
`stryker-close`, `controlled-close` and `evidence-gate` passed; `qualify-host` and
`contract-change-report` were skipped, as on every manual run (`ci-43150094060/run.json`).

All 17 mutants detected on the hosted runner in 20.9 to 21.7 s each (baseline 21.0 s), none above 30 s,
a third of the 90 s budget (`ci-43150094060/mutations.log`; budget risk, lesson 3: none seen).
The hosted runner took about 2.1 times as long as the local run (9.1 to 10.1 s).
The artifact files kept are the protocol job's `mutations.log`, `job.json` (`protocol-job.json`) and
the run record. The other artifacts are not committed.
