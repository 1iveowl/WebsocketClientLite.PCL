# Reconnect disposal schedules: evidence

Made 2026-11-25. The task: close the test gaps of the reconnect lifecycle that
[reconnect-decision.md](../../../verification/reconnect-decision.md) found
("Seeded defects, both ways" and the last paragraph of "Recommendation: do not adopt"),
with controlled schedules. Tests, the seeded-defect catalog and evidence only; no
production file changed. The summary is in that report's section
"Follow-up of 2026-11-25: the surviving decision-line defects".

Host: Ubuntu 24.04.5 LTS dev container, kernel 7.3.4+deb13-amd64, 32 CPUs, .NET SDK
11.0.100, test host .NET 11.0.0 (the test project targets net11.0; `-p:LibTfm` picks
the library build). Release configuration throughout.

The scratch directory's absolute path is replaced with `<scratch>` in every file here,
ignoring case, and also inside each `raw.zip`. Each `raw.zip` holds the per-run TRX
files, `dotnet` logs and build logs of its folder; `results.json` beside it is the
summary that the tables below are read from.

## Commits

| Commit | What |
| --- | --- |
| `08679d0` | `origin/main` at the start (merge of `releases/10.6.0`, #188). `ClientWebSocketRx.cs` is the same as at `fa96d59`, the commit the defects were first seeded in |
| `8a7b970` | The two new tests, first version |
| `5d5bb48` | `ConnectAfterDisposalInstallsNoSender` subscribes off the test thread (the schedule fix below) |
| `b528666` | The three decision-line defects added to `./scripts/pdd.sh reconnect-defect` |

## Part 1: the six defects, before and after

Script: [decision_defects.py](../../reconnect-decision/after/decision-defects/decision_defects.py),
unchanged from the 11 November runs, with catalog [part1/catalog-main.json](part1/catalog-main.json):
the six edits of `catalog-before.json`, unchanged, on side `main` (`08679d0`) and side
`branch` (`b528666`). Per defect: build (Release, library net10.0, as on 11 November),
the reconnect suite three times, the other tests once. The unchanged copy must pass
first. Command, from the repository root:

```text
python3 -I decision_defects.py part1/catalog-main.json <side> <scratch>/<copy> <scratch>/<out>
```

- `part1/main/`: side `main`, 2026-11-25 12:06:07 to 12:14:16 UTC. The first launch
  (`attempt-1-console.txt`) failed before running anything: it was wrapped in
  `/usr/bin/time`, which this host does not have (exit 127). Nothing was measured.
- `part1/branch/`: side `branch`, 12:19:40 to 12:27:45 UTC.

Reconnect suite: 11 cases at `08679d0`, 15 at `b528666`. Other tests: 319 cases in both.

| Defect | Rows | `main` reconnect (3 runs) | `main` other | `branch` reconnect (3 runs) | `branch` other | Failing test and line, every run |
| --- | --- | --- | --- | --- | --- | --- |
| unchanged copy | | passed | passed | passed | passed | |
| `subscribe-ignores-disposal` | 4 to 6 | survived | survived | survived | survived | |
| `subscribe-keeps-generation` | 1 to 3 | **detected** | survived | detected | survived | `EndedConnectionReportsNotConnected`, both rows: "IsConnected did not become false when the connection ended (…): False, True" |
| `register-ignores-disposal` | 10 to 12 | survived | survived | **detected** | survived | `RegisterAfterDisposalAdmitsNoService`, both rows: "Disposed client registered a service" |
| `connect-ignores-disposal` | 16 to 18 | survived | survived | **detected** | survived | `ConnectAfterDisposalInstallsNoSender`, both rows: "Disposed client installed a sender (first settlement: WebsocketConnected)" |
| `terminate-ignores-generation` | 19, 21, 22, 24 | detected | survived | detected | survived | `TerminalCallbackSuccessorSurvivesOldCleanup`: "Old cleanup reset successor connectivity" |
| `dispose-not-permanent` | 25 to 27 | detected | survived | detected | survived | `DisposedClientRejectsAdmission`, both rows: "Disposed client admitted a connection"; at `branch` also both rows of each new test |

Every verdict held in all three reconnect runs; no run was inconclusive or timed out.

## Part 2: the new tests, 50 repetitions each way

Script: [repeat_runs.py](repeat_runs.py). It builds a copy once (Release, library
net11.0) and runs one filter N times, each run with its own TRX. A run counts as
failed only if every selected row failed on the named assertion; nothing is retried.

```text
python3 -I repeat_runs.py <scratch>/<copy> <scratch>/<out> net11.0 50 "<filter>" ["<assertion>"]
```

The edits, as diffs: `part2/attempt-2/<defect>/<defect>.diff` (the same lines as in
Part 1). The SHA-256 of `ClientWebSocketRx.cs` in each copy as built is in
`part2/attempt-2/identity.txt`.

### Attempt 1 at `8a7b970` (kept; the schedule was wrong)

`part2/attempt-1/`, 30 runs each, 12:09:34 to 12:12:56 UTC, at the same time as the
`main` run of Part 1:

| Copy | Filter | Result |
| --- | --- | --- |
| `register-ignores-disposal` | `RegisterAfterDisposalAdmitsNoService` | run and failed, 30 of 30, "Disposed client registered a service" |
| `connect-ignores-disposal` | `ConnectAfterDisposalInstallsNoSender` | run and failed, 30 of 30, "Disposed client installed a sender (first settlement: WebsocketConnected)" |
| untouched | both tests | **mixed: 25 passed, 5 inconclusive**. In each of the 5, the `"dispose"` row of `ConnectAfterDisposalInstallsNoSender` failed after 15.04 seconds with "The schedule did not hold the handshake until disposal returned" |

That is a failure of the new test on unmodified code, so it was treated as a suspected
defect first and reproduced (rule 7). `part2/diagnostic-hold-thread/` is a copy of
`8a7b970` with only the diagnostic in `diagnostic.diff`: it records which thread runs the
hold. 30 runs: 26 passed, 4 failed, and every failure says "test thread 13, hold thread
13" (or 7 and 7). The handshake had completed synchronously inside `Subscribe` on the
test thread, so the hold blocked the thread that had to dispose the client, until the
15-second limit. The cause is the test's schedule, not the library: after the limit the
unmodified code installs the sender because the client is not yet disposed, which is
correct. Fix, `5d5bb48`: subscribe from `Task.Run`, as `RegisterAfterDisposalAdmitsNoService`
already did. The defect runs of attempt 1 failed on the named assertion, but the test
changed afterwards, so they are not counted; attempt 2 repeats every run on the new commit.

### Attempt 2 at `b528666`

`part2/attempt-2/`, 50 runs each, run alone, 12:15:58 to 12:19:23 UTC:

| Copy | Filter | Result | Wall seconds per run |
| --- | --- | --- | --- |
| untouched | both tests, 4 rows | run and passed, 50 of 50 | 0.92 to 0.97 |
| `register-ignores-disposal` | `RegisterAfterDisposalAdmitsNoService`, 2 rows | run and failed, 50 of 50, both rows: "Disposed client registered a service" | 1.47 to 1.57 |
| `connect-ignores-disposal` | `ConnectAfterDisposalInstallsNoSender`, 2 rows | run and failed, 50 of 50, both rows: "Disposed client installed a sender (first settlement: WebsocketConnected)" | 1.47 to 1.62 |

Case durations on the untouched copy, 50 runs (TRX):

| Case | Min | Median | Max |
| --- | ---: | ---: | ---: |
| `RegisterAfterDisposalAdmitsNoService("dispose")` | 0.021 s | 0.023 s | 0.027 s |
| `RegisterAfterDisposalAdmitsNoService("async")` | 0.001 s | 0.002 s | 0.004 s |
| `ConnectAfterDisposalInstallsNoSender("dispose")` | 0.024 s | 0.026 s | 0.031 s |
| `ConnectAfterDisposalInstallsNoSender("async")` | 0.001 s | 0.002 s | 0.005 s |

Neither test waits for a timer on the passing path. Why the `"dispose"` rows take about
0.02 seconds longer than the `"async"` rows was not established.

## Part 3: the seeded-defect catalog

`b528666` adds `subscribe-keeps-generation`, `register-ignores-disposal` and
`connect-ignores-disposal` to `ReconnectCases` in `tools/Pdd/ReconnectDefects.cs`, beside
`missing-removal`, and the two new tests to `ReconnectCaseCounts`. The validator now
requires one experiment per catalog entry (it required exactly one), and requires every
row of the named test to fail on the named assertion. It no longer reuses the boundary
validator, which expected exactly one failing row. `Pdd.Tests` gains the case
`partial-rows`: one row of a two-row theory passing with the defect seeded does not count.

`pdd-reconnect-defect/`: `./scripts/pdd.sh reconnect-defect --output <scratch>/pdd-reconnect`
at `b528666`, 12:27:45 to 12:28:12 UTC: run and passed. The baseline ran the 15 reconnect
cases and `RecordHostRuntime`; each of the four edits failed its named test on its named
assertion.

## Checks at `b528666`

| Check | Result |
| --- | --- |
| `./test-all-tfms.sh Release` (`checks/test-all-tfms-summary.txt`) | run and passed: 334 of 334 on each of netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 and net11.0, test host .NET 11.0.0 |
| `dotnet test tools/Pdd.Tests` | run and passed: 319 of 319 |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| `./scripts/pdd.sh reconnect-defect` | run and passed, as above |
| Coverage gate, Dafny, Stryker, the other `capture` jobs | not run: no production, contract or catalog file of theirs changed |
