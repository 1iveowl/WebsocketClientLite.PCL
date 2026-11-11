# Checks of step S4 after the retention count fix, made on 2026-11-11

The owner chose option 1 of the question on PR #140: `tools/Pdd/RetentionDefects.cs`
requires 40 retention baseline results instead of 14, and `RetentionCaseCounts` names
`InvalidHeadersRejectBeforePayloadRead` (20 cases) and
`ConfiguredLimitAcceptsLimitAndRejectsSuccessorFromHeader` (6 cases). The change is commit
`90c156f`. Because the tool is also used by host qualification and the aggregate, those now
require both theories with these counts as well.

Every check ran once, in the same order and with the same commands as the
[first run](../checks/README.md), on the working checkout at `90c156f` with a clean tree
before and after (`dirty=0` in [checks-run.log](checks-run.log)). The commands are in
[checks.sh](checks.sh). None was repeated.

| Check | Result |
| --- | --- |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| Pdd tests | run and passed: 262 of 262. The retention fixture builds its baseline from `RetentionCaseCounts`, so it now holds 40 results |
| `./test-all-tfms.sh Release` | run and passed: 307 of 307 on each of the five targets, 133 s |
| Coverage gate | run and passed: 83.1 % lines, 77.6 % branches, minimum 70 % |
| `capture --job protocol` | run and passed, 190 s. Verifier: `Dafny program verifier finished with 41 verified, 0 errors`. Baseline passed, all 17 mutants detected, 8.5 to 9.5 s each against a 90 s budget, none above a third of it |
| `capture --job tests` | run and passed, 164 s |
| `capture --job controlled` | run and passed, 67 s: `defect`, `boundaries`, `queued`, `leases`, `retention` and `reconnect` passed |
| `capture --job stryker` | run and passed, 365 s. Dispositions matched; `verification/stryker-close-dispositions.json` unchanged |
| `aggregate` | run and passed: every test, proof and mutation obligation passed; review obligations are not run |

The full capture set is in [capture-90c156f.zip](capture-90c156f.zip), and the aggregate
summary in [capture-summary.md](capture-summary.md).

## Stryker

All 249 outcomes match the run on `7390266`. Against S2 the same single outcome differs:
the statement mutation that removes `throw handshakeException;` in
`WebsocketConnectionHandler.cs`, line 95, was killed in S2 and survived in both S4 runs.
It has no recorded disposition. The cause is below.

### Why the line 95 mutant survived

Investigated on 2026-11-11, after S4's receipt was posted. In short: the mutant is a
real defect, and the only test that can see it does so through a race.

- **What the mutant does.** Without `throw handshakeException;`, the first branch of
  `ConnectWebsocket` is empty, so a handshake that reports an error skips the
  `else if`/`else` and reaches `setSenderAction`. That installs `Sender`, marks the
  client connected and starts the client ping, as for an accepted handshake.
- **Why the message tests pass under it.** `HandshakeParser.cs`, lines 40 and 41,
  reports a rejected response twice: as the handshake result, and as an `Aborted`
  status. `WebsocketClientFactory.cs`, line 59, turns the `Aborted` status into
  `OnError`. The subscriber therefore has the original error, for example
  `Invalid WebSocket handshake response (HTTP 404): ...`, before `ConnectWebsocket`
  reaches line 95. Under the mutant, `Non101HandshakeResponse_FailsFast_WithStatusCode`
  and `InvalidAcceptNeverConnectsEvenWhenServerStaysOpen` see the same error and pass.
- **Why the kill is a race.** All three runs (S2 and both S4 runs) record the same four
  covering tests. S2's killer was `ReconnectTests.FailedConnectTerminatesAndRemovesRegistration`.
  It asserts that `client.Sender` is null right after the terminal error. Under the mutant
  the connect code goes on and sets `Sender` on another thread, so the test fails only
  when that happens before the assertion.
- **Measured** on `git archive` copies of `c9701aa`, net10.0, running the four covering
  tests:

  | Copy | Runs | Failed |
  | --- | --- | --- |
  | Mutant | 15 plain | 0 |
  | Mutant | 15 with every core loaded | 6 |
  | Mutant | 15 more, loaded | 5, each `Assert.Null` on `Sender` in `FailedConnectTerminatesAndRemovesRegistration` |
  | Unchanged | 15 loaded | 0 |

  Stryker runs mutants in parallel, so the load decides the outcome. These runs were
  made in a temporary scratch directory and are not kept here as files. Step S11,
  proposed for `docs/pdd-10.3-session-plan.md` in PR #141, repeats them as kept evidence.
- **What S2 cannot show.** S2's Stryker logs keep no per-test detail, so its kill is
  attributed to this race, not shown. Since S2's commit `911f2d4`, the only production
  change is `TcpConnectionService.cs`, which uses `AbortedUnexpectedlyMessage` with the
  same text. It does not touch the handshake path.
- **What follows.** S11 adds a test that awaits `ConnectWebsocket` directly against a 404
  server, and asserts that it throws and that `setSenderAction` never runs. That kills
  the mutant without depending on timing. Lesson 10, in the same PR, records the rule: when a
  Stryker outcome changes and the code did not, run the covering tests against a copy
  with the mutant, plain and under load, before calling it noise. The same account is in
  PR #140, decision 5.

## CI

Manual run [43081805779](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/43081805779)
on `90c156f`: **run and passed**. `build-and-test`, `verify-protocol`, `stryker-close`,
`controlled-close` and `evidence-gate` passed; `qualify-host` and `contract-change-report`
were skipped. In `verify-protocol` all 17 mutants were detected in 14.4 to 15.1 s each
against a 90 s budget, none above a third of it
([ci-43081805779/mutations-results.json](ci-43081805779/mutations-results.json)). The
aggregate summary is [ci-43081805779/summary.md](ci-43081805779/summary.md).
