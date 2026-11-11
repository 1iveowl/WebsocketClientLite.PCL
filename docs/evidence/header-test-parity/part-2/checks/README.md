# Checks of step S4, made on 2026-11-11

Every check ran once, in this order, on the working checkout at `7390266` with a clean
tree before and after (`dirty=0` in [checks-run.log](checks-run.log)). The commands are in
[checks.sh](checks.sh). Each `<check>.log` holds the output and `<check>.exit` the exit
code and duration. None was repeated.

| Check | Result |
| --- | --- |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| Pdd tests, `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release` | run and passed: 262 of 262 |
| `./test-all-tfms.sh Release` | run and passed: 307 of 307 on each of netstandard2.0, netstandard2.1, net8.0, net9.0 and net10.0 (278 after S3, plus 29 S4 cases), 132 s |
| Coverage gate, `scripts/check-coverage.sh` over a fresh coverage run | run and passed: 83.1 % lines, 77.6 % branches, minimum 70 % |
| `capture --job protocol` | run and passed, 186 s. `scripts/dafny-masking.sh --check`: last line `Dafny program verifier finished with 41 verified, 0 errors`. Mutation suite: baseline passed, all 17 mutants detected, 8.5 to 9.3 s each against a 90 s budget, none above a third of it |
| `capture --job tests` | run and passed, 165 s |
| `capture --job controlled` | **run and failed**, 50 s. `defect`, `boundaries`, `queued` and `leases` passed. `retention` failed: `Incomplete retention baseline cases`. `reconnect` was not reached |
| `capture --job stryker` | run and passed, 283 s. Recorded dispositions matched; `verification/stryker-close-dispositions.json` unchanged. One outcome differs from S2, below |
| `aggregate` | **run and failed**: `controlled/retention` and `controlled/reconnect` have no successful command, so `REASSEMBLY-001-TEST` and `EPOCH-001-TEST` fail. Every other obligation passed; review obligations are not run |

The full capture set is in [capture-7390266.zip](capture-7390266.zip), and the
aggregate summary in [capture-summary.md](capture-summary.md).

## The controlled capture failure

`tools/Pdd/RetentionDefects.cs`, line 85, requires the retention baseline, which runs
every case of `ParserRetentionTests` and `RuntimeEvidenceTests.RecordHostRuntime`, to
have exactly 14 results. It has 40: the 13 cases the tool lists, the runtime case, 20
cases of `InvalidHeadersRejectBeforePayloadRead` (9 from S3, 11 from S4) and 6 of
`ConfiguredLimitAcceptsLimitAndRejectsSuccessorFromHeader`. All 40 passed, and both
retention defects were run and detected; only the count check failed.

S3 did not run the capture jobs. A run of `./scripts/pdd.sh retention-defects` on a
worktree of `4c449d2`, S3's last commit, failed the same way with 23 baseline cases
([retention-4c449d2/](retention-4c449d2/)). The failure started with S3's theory.

## CI

Manual run [43062905406](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/43062905406)
on `7390266`: **run and failed**. `build-and-test`, `verify-protocol` and `stryker-close`
passed. `controlled-close` failed at the same step (`retention: failed`), so
`evidence-gate` failed. `qualify-host` and `contract-change-report` were skipped. In
`verify-protocol` all 17 mutants were detected in 10.5 to 11.5 s each, against about
21 s in runs 43041451305 and 43046774633
([ci-43062905406/mutations-results.json](ci-43062905406/mutations-results.json)).

## Stryker against S2

Compared by file, mutator and location with S2's capture (`capture-911f2d4.zip` in
`docs/evidence/header-traceability/`), 248 of 249 outcomes are the same. One differs: the
statement mutation that removes `throw handshakeException;` in
`WebsocketConnectionHandler.cs`, line 95, was killed in S2 and survived here. The
production code is unchanged since S2, and S4 changed only parser tests, so the cause is
not established. The mutant has no recorded disposition, so the Stryker job passes.
`stryker-close-dispositions.json` was not edited.
