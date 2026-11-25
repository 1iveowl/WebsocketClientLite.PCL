# Caller-side Close defects and synchronous Dispose: evidence

Made 2026-11-25. The report is [verification/close-caller-defects.md](../../../verification/close-caller-defects.md).
No production file, disposition or gate changed.

Host: Ubuntu 24.04.5 LTS dev container, kernel 7.3.4+deb13-amd64, 32 CPUs, .NET SDK
11.0.100, test host .NET 11.0.0 (`-p:LibTfm=net11.0` throughout), Release. The scratch
directory's absolute path is replaced with `<scratch>` in every file here, ignoring case,
and also inside each `raw.zip`, including paths that a console log wrapped across lines.
Each `raw.zip` holds that folder's TRX files, logs and reports; the JSON beside it is the
summary that the tables are read from.

During the Part A exploration (12:36 to 13:05 UTC), a review agent for #189 built and ran
tests in its own worktree on the same host for part of the time.

## Commits

| Commit | What |
| --- | --- |
| `b7a6bcc` | `releases/10.6.0`, the base. `WebsocketConnectionHandler.cs` SHA-256 `fab51b41…`, the dispositions' hash |
| `c3a42ed` | `SynchronousDisposeReleasesTransport`, first version |
| `9d1e8d7` | The test runs the harness cleanup itself, so that a cleanup failure cannot replace the named one |
| `2547658` | `close-caller-defects` added to `tools/Pdd` |
| `e6fc58a` | `release-early` names a deterministic test |

## Part A

- [catalog.json](catalog.json): the 17 defects, each with its edits, kind and the requirement
  it breaks.
- [close_caller_defects.py](close_caller_defects.py): the runner, adapted from the
  reconnect report's `decision_defects.py`: ControlledCloseTests and CloseBoundaryTests
  three times, the whole project once (`FULL_RUNS` repeats it; added after the first
  recorded run). Command:
  `python3 -I close_caller_defects.py catalog.json base <scratch>/<copy> <scratch>/<out> [<id>...]`.
  Its own verdict "detected" counts every failure, including timeouts. The report counts
  only failures on an assertion, read from each run's `not_passed` list in `results.json`.
- `part-a/attempt-1/`: 12:35:40 to 12:36:10 UTC. The unchanged copy passed. Then the
  script stopped with `KeyError: 'rows'` (my adapted script still read a field this catalog
  lacks), after it had written the first edit into its copy. No defect was measured, and
  that copy was discarded.
- `part-a/exploration/`: all 17 defects at `b7a6bcc`, 12:36:54 to 13:05:43 UTC. The
  unchanged copy passed 19 of 19 three times and 330 of 330 once.
- `part-a/confirm-full-project/`: the four survivors and the three defects caught only by
  the whole project, with `FULL_RUNS=3`, 13:06:50 to 13:17:47 UTC. Every verdict held in
  every run.
- `part-a/probe-30/`: [probe_catalog.py](probe_catalog.py) runs each of the 13 catalog
  entries' named test 30 times with its edit, at `2547658`, 13:27:56 to 13:38:20 UTC
  (`summary.json`, per row: runs failing on the named assertion, failing otherwise,
  passing). Its first launch stopped at once: its regular expression read 10 of the 13
  entries (`attempt-1-console.txt`). Result: 12 entries 30 of 30. `release-early` with
  `PeerWaitEndsByControlledTimeoutOrReadFailure`: some row failed in 28 of 30 runs, a
  named failure in 24. The same edit with `ProtocolStateTests.CloseResponseTimeoutMakesTheTransportTerminal`:
  30 of 30 on "Collections differ" (`raw.zip`, `release-early-timeout-test/`).
- `part-a/pdd-close-caller-defects-1/`: `./scripts/pdd.sh close-caller-defects` at
  `2547658`, 13:23:31 to 13:23:40 UTC: **run and failed**, "Missing intended close caller
  assertion" on `release-early`, where one of the two PeerWait rows passed. This failure
  is what led to the 30-run probe.
- `part-a/pdd-close-caller-defects-2/`: the same at `e6fc58a`, 13:40:23 to 13:40:57 UTC:
  run and passed, 13 of 13. `dirty_worktree: true` in both runs: this evidence folder was
  untracked, and no source input differed.

## Part B

The edits: `part-b/attempt-2/<edit>/<edit>.diff`. `part-b/attempt-2/identity.txt` has the
SHA-256 of `WebsocketConnectionHandler.cs` in each copy as built (untouched: `fab51b41…`).
Runner: `repeat_runs.py` of `docs/evidence/reconnect-disposal-schedules/` (#189), 30 runs.

| Attempt | Copy | Filter | Result |
| --- | --- | --- | --- |
| 1, `c3a42ed` | untouched | both rows | run and passed, 30 of 30 |
| 1 | `sync-transport-release` (line 373 removed) | both rows | run and failed, 30 of 30, "Synchronous Dispose did not release the transport" |
| 1 | `sync-close-guard-or` (line 353 `&&` to `\|\|`) | `no-close` row | **inconclusive, 30 of 30**: the test failed with a raw `NullReferenceException` from the harness cleanup, which replaced the named failure. Fixed in `9d1e8d7` |
| 2, `2547658` | untouched | both rows | run and passed, 30 of 30; `no-close` 0.007 to 0.011 s, `close-finished` 0.016 to 0.021 s |
| 2 | `sync-transport-release` | both rows | run and failed, 30 of 30, both rows, "Synchronous Dispose did not release the transport" |
| 2 | `sync-close-guard-or` | `no-close` row | run and failed, 30 of 30, "Synchronous Dispose threw: System.NullReferenceException" |

## Stryker

- `stryker/gate/`: `./scripts/pdd.sh capture --job stryker` at `e6fc58a` (the gate's own
  configuration, 8 workers), 13:40:57 to 13:47:26 UTC: run and passed. `stryker-triage.json`:
  passed against the unchanged dispositions. `WebsocketConnectionHandler.cs`: 43 Killed,
  26 Survived, 7 NoCoverage, 3 CompileError, 26 Ignored.
- `stryker/sender-exploratory-c8/`: a copy of `e6fc58a` whose `stryker-config.json`
  mutates `**/Service/WebsocketSenderHandler.cs` instead (`config.diff`), run with
  `./scripts/stryker-close.sh`, 8 workers, 13:47:26 to 13:56:03 UTC: run and completed,
  8 minutes 37 seconds. 166 tested: 112 Killed, 53 Survived, 11 NoCoverage, 1 Timeout,
  3 CompileError, 43 Ignored, score 63.84 %.
- `stryker/sender-exploratory-c2/`: the same copy with `--concurrency 2`, as CI runs the
  gate, 13:56:56 to 14:27:01 UTC: run and completed, **30 minutes 4 seconds**. 110 Killed,
  53 Survived, 11 NoCoverage, 3 Timeout, 3 CompileError, 43 Ignored, score 63.84 %.

## Checks

| Check | Result |
| --- | --- |
| `./test-all-tfms.sh Release` at `2547658` | run and passed, 13:24:09 to 13:26:49 UTC: 332 of 332 on each of netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 and net11.0 (`checks/test-all-tfms-summary.txt`) |
| `dotnet test tools/Pdd.Tests` at `e6fc58a` | run and passed: 331 of 331 |
| `./scripts/pdd.sh validate` at `e6fc58a` | run and passed: 16 requirements |
| Coverage gate, Dafny, other capture jobs | not run: no production or contract file changed |
