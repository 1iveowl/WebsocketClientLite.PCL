# Rejected-handshake test: baseline, failing-first runs and checks (2026-11-11)

Step S11 of `docs/pdd-10.3-session-plan.md` (lesson 10, PR #140 decision 5). Every file
here was made on 2026-11-11 on the S11 worktree, base `29697a4`
(`origin/releases/10.3.0.0`). These are local measurements on a 32-core machine on which
other step sessions ran at the same time (see "Load and the timing lock").

## The mutant

`tools/defect.py throw-removed` replaces line 95 of
`src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs`,
`throw handshakeException;`, with `;`. That is Stryker's statement mutation at that line
(mutant 573 in the #140 review capture). The script asserts that line 95 is the throw and
that it occurs once. Each copy's diff is in `copies/<copy>.diff`, its file hashes in
`copies/<copy>.identity.txt` and its build in `copies/<copy>.build.log`.

| Copy | Source | Change |
| --- | --- | --- |
| `base-mutant` | `git archive 29697a4` | `throw-removed` |
| `base-unchanged` | `git archive 29697a4` | none |
| `new-mutant` | `git archive 4f3c6f0` (the new test) | `throw-removed` |
| `new-unchanged` | `git archive 4f3c6f0` | none |

## Commands

```bash
tools/prepare.sh <copy> <commit> <throw-removed|none>      # untimed: copy, diff, build
flock ~/worktrees/.timing.lock tools/batch.sh <batch> <copy> <runs> <plain|loaded> <covering|covering+new>
python3 -I tools/summarize.py runs <batch>...
```

`batch.sh` runs `dotnet test --no-build -c Release` on the prebuilt copy, once per run,
and keeps `run-NN.log`, `run-NN.trx` and `run-NN.exit` (exit code and milliseconds) under
`runs/<batch>/`. `covering` is the four test cases Stryker lists as covering line 95:
`ReconnectTests.FailedConnectTerminatesAndRemovesRegistration`,
`IntegrationTests.Non101HandshakeResponse_FailsFast_WithStatusCode` and both rows of
`IntegrationTests.InvalidAcceptNeverConnectsEvenWhenServerStaysOpen`. `covering+new` adds
the four rows of `HandshakeRejectionTests.RejectedHandshakeThrowsAndNeverInstallsSender`.
Loaded means one `yes > /dev/null` per core (32) for the whole batch.

## Baseline (`baseline-summary.txt`)

Made before the new test existed, from copies of the clean base `29697a4`.

| Batch | Copy | Runs | Runs that failed | Failing test |
| --- | --- | --- | --- | --- |
| `baseline-mutant-plain` | `base-mutant` | 15 plain | 7 | `FailedConnectTerminatesAndRemovesRegistration`, `Assert.Null` on `Sender`, each time |
| `baseline-mutant-loaded` | `base-mutant` | 15 loaded | 6 | the same |
| `baseline-unchanged-loaded` | `base-unchanged` | 1 loaded | 0 | |

The three message tests passed in all 31 runs. This repeats the post-S4 measurement on
`c9701aa` (mutant: 0 of 15 plain, 11 of 30 loaded; unchanged: 0 of 15 loaded) with one
difference: here 7 of 15 plain runs failed, not 0. Other sessions' processes were present
in the plain batch too (`processes-start.txt`), so "plain" means no load of this
session's own, not an idle machine. Either way the kill depends on timing.

## Failing first (`failing-first-summary.txt`)

| Batch | Copy | Runs | New test, each of 4 rows | `FailedConnectTerminatesAndRemovesRegistration` |
| --- | --- | --- | --- | --- |
| `new-mutant-plain` | `new-mutant` | 15 plain | failed 15 of 15 | passed 15 of 15 |
| `new-mutant-loaded` | `new-mutant` | 15 loaded | failed 15 of 15 | passed 8 of 15 |
| `new-unchanged-loaded` | `new-unchanged` | 15 loaded | passed 15 of 15 | passed 15 of 15 |

All 120 failures of the new test in the mutant copy are its first assertion,
`Assert.Equal(0, senderCalls)` with the actual value 1: `setSenderAction` ran. No run of
the new test was inconclusive and none passed against the mutant.

## Load and the timing lock

Each batch ran inside one hold of `~/worktrees/.timing.lock` (controller decision 6 of
2026-11-11), so no other session's timed or heavy run overlapped it. The process list
(`ps -eo pid,etime,comm | grep -E 'claude|dotnet'`, names only) is in
`runs/<batch>/processes-start.txt` and `processes-end.txt`. Every batch shows 7 `dotnet`
processes that had been running for about 55 minutes (build servers of other sessions)
and 5 to 7 `claude` sessions. Builds by other sessions do not take the lock, so they may
have run during a batch.

## Checks

See `checks/README.md`.
