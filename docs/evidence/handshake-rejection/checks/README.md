# S11 checks on `ee72d04` (2026-11-11)

Command: `checks.sh` in this directory, run once from the S11 worktree. Each heavy step
held `~/worktrees/.timing.lock` (`flock -o`) for its whole run; `<step>.processes.txt`
records how long it waited, `uptime` and the process list (`ps -eo pid,etime,comm`,
names only) at the start and the end of the step. `<step>.exit` holds the exit code and
seconds. The logs and the capture set are in `capture-ee72d04.zip`; the aggregate summary is
`capture-summary.md`.

| Check | Lock | Result |
| --- | --- | --- |
| `dotnet build src/main/WebsocketClientLite.sln -c Release` | no | run and passed, 0 warnings |
| `./scripts/pdd.sh validate` | no | run and passed: 16 requirements |
| Pdd tests (`tools/Pdd.Tests`) | no | run and passed: 262 of 262 |
| `./test-all-tfms.sh Release` | yes | run and passed: 311 of 311 on each of the five library targets (307 before, plus the four new rows) |
| Coverage tests and `scripts/check-coverage.sh ... 70` | tests yes, gate no | run and passed: 83.9 % lines, 77.7 % branches |
| `capture --job protocol` | yes | run and passed: `Dafny program verifier finished with 41 verified, 0 errors`; 17 of 17 mutants detected at their clauses in 9.2 to 10.5 s each (budget 90 s, none above a third of it) |
| `capture --job tests` | yes | run and passed |
| `capture --job controlled` | yes | run and passed |
| `capture --job stryker` | yes | run and passed: dispositions matched; `verification/stryker-close-dispositions.json` not edited |
| `aggregate` | no | run and passed |

## Stryker

`stryker-against-review-5200f94.txt` compares this report with the #140 review capture on
`5200f94` (`docs/evidence/header-test-parity/review-2026-11-11/checks/capture-5200f94.zip`),
with `tools/stryker-compare.py`. 76 mutants were tested in both: 42 killed, 27 survived,
7 without coverage, and no outcome changed.

- The statement mutation at `WebsocketConnectionHandler.cs`, line 95 (`throw handshakeException;`
  becomes `;`) is **killed**, and its only killer is
  `HandshakeRejectionTests.RejectedHandshakeThrowsAndNeverInstallsSender`. Its covering tests are
  the new test and the four earlier ones. In the review capture it was killed by the racing
  `FailedConnectTerminatesAndRemovesRegistration`, and in both S4 captures it survived, so against
  S4 this outcome changes from survived to killed.
- The equality mutation at line 93 stays killed; its killers differ from the review capture
  (`ReconnectTests.SequentialRepeatOrRetryUsesFreshServices` and
  `TerminalCallbackSuccessorSurvivesOldCleanup` here). The status is the same.

## Dirty worktree

The receipt records `dirty_worktree: true` for every job after `pdd-tests`. The only change
was the untracked file `docs/evidence/handshake-rejection/README.md`, written while the
checks ran. No source, test or verification file differed from `ee72d04`. It is reported
here and not rerun. The manual CI run on the branch head checks a clean checkout.
