# Checks on `3931441`, after the #144 review (2026-11-12)

Command: `TAG=review ../../checks/checks.sh`, run once from the S11 worktree on a clean tree.
This is the script fixed for finding 7: it stops at the first failed step. It did not stop.
`checks-driver.txt` is its output. Every step reports `dirty=0`, the tree was clean at the
end too, and every capture receipt says `dirty_worktree: false`. Each heavy step held
`~/worktrees/.timing.lock` (`flock -o`) for its whole run and waited 0 s for it.
`<step>.processes.txt` holds `uptime` and the process list (`ps -eo pid,etime,comm`, names
only) at the start and the end of the step. `<step>.exit` holds the exit code and seconds.
The logs and the capture set are in `capture-3931441.zip`, and the aggregate summary is
`capture-summary.md`.

| Check | Lock | Result |
| --- | --- | --- |
| `dotnet build src/main/WebsocketClientLite.sln -c Release` | no | run and passed |
| `./scripts/pdd.sh validate` | no | run and passed: 16 requirements |
| Pdd tests (`tools/Pdd.Tests`) | no | run and passed: 262 of 262 |
| `./test-all-tfms.sh Release` | yes | run and passed: 311 of 311 on each of the five library targets |
| Coverage tests and `scripts/check-coverage.sh ... 70` | tests yes, gate no | run and passed: 83.9 % lines, 77.7 % branches |
| `capture --job protocol` | yes | run and passed: `Dafny program verifier finished with 41 verified, 0 errors`; 17 of 17 mutants detected in 8.5 to 9.5 s each (budget 90 s, none above a third of it) |
| `capture --job tests` | yes | run and passed |
| `capture --job controlled` | yes | run and passed |
| `capture --job stryker` | yes | run and passed: dispositions matched; `verification/stryker-close-dispositions.json` not edited |
| `aggregate` | no | run and passed: machine evidence gate passed |

## Stryker

`stryker-against-s11-ee72d04.txt` compares this report with S11's capture on `ee72d04`
(`../../checks/capture-ee72d04.zip`), using `../../tools/stryker-compare.py`. 76 mutants
were tested in both: 42 killed, 27 survived, 7 without coverage. No outcome changed.

- The statement mutation at `WebsocketConnectionHandler.cs`, line 95 (mutant 573,
  `throw handshakeException;` becomes `;`), is **killed**. Its only recorded killer is
  **`ReconnectTests.FailedConnectTerminatesAndRemovesRegistration`, the racing test**, not
  `HandshakeRejectionTests.RejectedHandshakeThrowsAndNeverInstallsSender`. The new test
  is among its covering tests. In S11's capture and in S11's CI run, the new test was its
  only killer.
- Why: `stryker-config.json` does not set `disable-bail`. In Stryker.NET 5.0.0 that
  option is described as "Disable abort unit testrun as soon as the first unit test
  fails". So the run for a mutant stops at its first failing test, and `killedBy` lists
  only the tests that had failed by then. The test classes run in parallel. Under this
  mutant the racing test fails in about half of all runs (11 of 15 plain and 6 of 15 loaded
  in `../runs/`), and it fails at about the same moment as the new test's rows. Which of
  the two is recorded is therefore a race. Whether the mutant is detected is not a race:
  the new test failed in all 30 mutant runs of `../runs/`.
- The Stryker run was not repeated to get a different attribution (Common rules,
  "Evidence"). Whether this meets "killed with the new test among its killers" is asked on
  #144.
- The equality mutation at line 93 stays killed, by
  `ErrorDeliveryStressTests.ReadLoopError_AlwaysReachesSubscriber_UnderRepeatedRuns` and
  `ReconnectTests.TerminalCallbackSuccessorSurvivesOldCleanup`.
