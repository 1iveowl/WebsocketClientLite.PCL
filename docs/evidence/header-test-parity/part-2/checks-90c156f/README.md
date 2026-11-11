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
Its cause is still not established, and it has no recorded disposition.

## CI

Manual run [43081805779](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/43081805779)
on `90c156f`: **run and passed**. `build-and-test`, `verify-protocol`, `stryker-close`,
`controlled-close` and `evidence-gate` passed; `qualify-host` and `contract-change-report`
were skipped. In `verify-protocol` all 17 mutants were detected in 14.4 to 15.1 s each
against a 90 s budget, none above a third of it
([ci-43081805779/mutations-results.json](ci-43081805779/mutations-results.json)). The
aggregate summary is [ci-43081805779/summary.md](ci-43081805779/summary.md).
