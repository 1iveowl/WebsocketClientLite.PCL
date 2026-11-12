# Review fix on #144: failing-first runs of the final test (2026-11-12)

Merge point M7. `/code-review high` on PR #144 found 8 issues. Findings 1, 2, 3 and 6
changed `HandshakeRejectionTests.RejectedHandshakeThrowsAndNeverInstallsSender`, so S11's
failing-first runs (`../runs/new-*`, on `4f3c6f0`) no longer measure the final test. This
folder repeats them on the final test, commit `46ca940`. Every file here was made on
2026-11-12 in the S11 worktree. S11's own runs are unchanged beside it.

## What changed in the test (`46ca940`)

| Finding | Change |
| --- | --- |
| 1 | The test connects the `TcpClient` itself and passes it to `TcpConnectionService`, so `ConnectTcpClient` skips its connect. The 500 ms timeout of the `Timeout` row bounds only the handshake. A slow loopback connect on a loaded host cannot end the row with `WebsocketClientLiteTcpConnectException` |
| 2 | `ConnectWebsocket` is awaited directly. The outer `WaitAsync(Limit)` of 10 s is gone. The handshake timeout (`HandshakeHandler`, Rx `Timeout`) is now the only bound, so the call has returned or thrown on every path before the assertions and before disposal |
| 3 | `Assert.Contains("404"/"101", thrown.Message)` is removed. `WebsocketClientLiteException` has no status code field, and the thrown error and the `Aborted` status error are separate instances, so there is no check that does not use the message. The narrowest one stays: the thrown message equals the `Aborted` status error's message. It compares two runtime values and uses no production text |
| 6 | The connection is declared before the server, so the server is disposed first, and the sender's peer-close delay completes at once. Only the mutant leaves a close handshake for `DisposeAsync`. That close now ends at once instead of waiting out its timeouts. Mutant runs took 1.2 to 6.8 s plain, against about 16 s in S11 |

## Commands

```bash
review-2026-11-12/run-all.sh    # writes run-all.log
```

`run-all.sh` sets `EVIDENCE` to this folder and `SCRATCH=/tmp/s11-review-scratch`, then
runs S11's `tools/prepare.sh` for both copies and `tools/batch.sh` for the three batches.
Each step holds `~/worktrees/.timing.lock` for its whole run. `run-all.log` records when
each step waited for the lock and when it got it. The builds ran under the lock too.

| Copy | Source | Change |
| --- | --- | --- |
| `review-mutant` | `git archive 46ca940` | `throw-removed` (`copies/review-mutant.diff`: line 95 becomes `;`) |
| `review-unchanged` | `git archive 46ca940` | none (`copies/review-unchanged.diff` is empty) |

Both builds passed (`copies/*.build.exit`, `copies/*.build.log`). File hashes are in
`copies/*.identity.txt`.

## Results (`summary-all-batches.txt`)

| Batch | Copy | Runs | New test, each of 4 rows | `FailedConnectTerminatesAndRemovesRegistration` |
| --- | --- | --- | --- | --- |
| `review-mutant-plain` | `review-mutant` | 15 plain | failed 15 of 15 | failed 11 of 15 |
| `review-mutant-loaded` | `review-mutant` | 15 loaded | failed 15 of 15 | failed 6 of 15 |
| `review-unchanged-loaded` | `review-unchanged` | 15 loaded | passed 15 of 15 | passed 15 of 15 |

Every one of the 120 failures of the new test in the mutant copy is its first assertion,
`Assert.Equal(0, senderCalls)` at line 98, with the actual value 1: `setSenderAction` ran.
No run is without a `.trx`, and no test is inconclusive in any run. The three message tests
passed in all 45 runs. The racing test still races against the mutant.

`summary-all-batches.txt` also reruns the fixed `summarize.py` over S11's six batches. Their
counts are the same as in `../README.md`, with 0 runs without a trx.

## Load and the lock

Loaded means 32 `yes > /dev/null` loops, one per core, for the whole batch
(`runs/<batch>/batch.txt`). "Plain" means no load from this session. The process lists
(`ps -eo pid,etime,comm`, filtered to `claude|dotnet`, names only) are in
`runs/<batch>/processes-start.txt` and `processes-end.txt`. They show 4 or 5 `claude`
sessions and no `dotnet` process at any batch start or end. Builds by other sessions do
not take the lock, so they may have run during a batch.

## Checks

See `checks/README.md`. Every check ran on `3931441` and passed. The line 95 mutant is
killed, but Stryker records only the racing test as its killer. The reason is explained
there.
