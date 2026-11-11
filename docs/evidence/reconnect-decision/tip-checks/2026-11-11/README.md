# Tip checks of PR #142, 2026-11-11

Made on 2026-11-11 for merge point M3, after the `/code-review high` of PR #142
(finding 4). S6's receipt reported build, the net10.0 test project, `pdd.sh validate`
and the Pdd tests at the tip `cfee74c` as run and passed, but kept no command or
output for them. That report has no evidence and is withdrawn; these runs replace
it. `../../branch-checks/` is S5's, made on `fa96d59` before the change, and says
nothing about the tip.

Tested commit: `55ad2f7`, the last commit that changes anything but this folder. The
commit that adds this folder changes only this folder. `git status --porcelain` was
empty at the start and at the end (`run.log`). Local measurements on one host, with
other step sessions running at the same time.

`run-tip-checks.sh` ran every check, each into its own log, and wrote each command,
its exit code and its times to `run.log`. Every heavy check held the shared lock
`~/worktrees/.timing.lock` (the lock times are in `run.log`): build, matrix, coverage
run, Pdd tests and the four capture jobs. `source-diff`, the coverage gate, `validate`
and `aggregate` did not.

```bash
bash run-tip-checks.sh /home/vscode/worktrees/s6 /home/vscode/worktrees/s6/artifacts/tip-2026-11-11
```

| Check | Log | Result |
| --- | --- | --- |
| Source equals the base `fa96d59` (`src`, `tools`, `scripts`, the Dafny sources, generated code, requirements, boundaries, mutation catalogs, Stryker dispositions, `test-all-tfms.sh`, `GitVersion.yml`) | `source-diff.log` | run and passed: empty diff, exit 0 |
| Release build of the solution | `build.log` | run and passed: 0 warnings, 0 errors |
| Five-target matrix, `./test-all-tfms.sh Release` | `matrix.log` | run and passed: 269 of 269 on each of the five library targets (test host .NET 10) |
| Coverage run and gate | `coverage-run.log`, `coverage-gate.log` | run and passed: 269 of 269; 83.1 % lines, 77.6 % branches (70 % required) |
| Pdd tests | `pdd-tests.log` | run and passed: 262 of 262 |
| `./scripts/pdd.sh validate` | `validate.log` | run and passed: 16 requirements |
| `capture --job protocol` | `capture-protocol.log` | run and passed: "41 verified, 0 errors"; 17 of 17 Dafny mutants detected, 9.36 to 10.80 s each against a 90 s budget, none above a third of it (`mutant-times.txt`) |
| `capture --job tests` | `capture-tests.log` | run and passed |
| `capture --job controlled` | `capture-controlled.log` | run and passed: defect, boundaries, queued, leases, retention, reconnect |
| `capture --job stryker` | `capture-stryker.log` | run and passed: recorded dispositions matched. The line 95 `throw handshakeException;` mutant of `WebsocketConnectionHandler.cs` survived this run, where S6's run at `bd743b3` killed it; the source is the same, and this is the race of lesson 10 that S11 addresses, so neither outcome is evidence |
| `aggregate` | `aggregate.log`, `summary.md` | run and passed: machine evidence gate passed; review evidence not-run |

`capture-55ad2f7.zip` holds the capture directories (`pdd/`) and the coverage report
(`coverage/`). `summary.md` is a copy of `pdd/summary.md` with its links turned into
plain paths inside the archive.

`check_decision_defects.py` checks the review fix of
`../../after/decision-defects/decision_defects.py` (finding 9) without running a
defect: a command that outlives its timeout is killed together with its child, and a
skipped test is counted as skipped. Output in `check_decision_defects.txt`: passed.

```bash
python3 -I check_decision_defects.py ../../after/decision-defects/decision_defects.py <empty directory>
```
