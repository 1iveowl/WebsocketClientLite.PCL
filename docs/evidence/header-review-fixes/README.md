# Evidence: fixes for the #136 code review (2026-11-11)

Made on 2026-11-11 after a `/code-review` of #136 at `9673862`. Its findings, and
what happened to each, are in the comment on #136. This folder covers findings 1,
2, 3, 8 and 9, which were fixed in `34c2421`. Findings 4 to 7 are step S10 of
`docs/pdd-10.3-session-plan.md`.

## Contents

- `ci-43041451305/`: the manual CI run on `9673862`, before the fixes. Run JSON with
  job results, and from the `pdd-protocol` artifact: `mutations-results.json`,
  `mutations.log`, `protocol-job.json` and `CLOSE-003-premature-release-stdout.log`.
  `verify-protocol` and `evidence-gate` failed. `build-and-test`, `controlled-close`
  and `stryker-close` passed. Mutants took about 21 seconds, against about 9
  locally. `CLOSE-003-premature-release` printed the expected postcondition error
  but exhausted the 90-second budget (`timed_out=true`, exit 137). The suite then
  stopped, so the header mutants were not run. The cause is not established.
- `replay-3/`: `RejectsMalformedFrames` with the announced payload appended,
  replayed on `a779602` (`old-commit.txt`). The test sources are
  `PayloadParsingTests.a779602.cs` (unchanged), `.padding-only.cs` (no message
  check) and `.padding-and-message.cs`, with `replay-test.diff`. The `a779602`
  variants use the literal message because that commit has no constant. Results:
  - padding only: run and failed, 7 of 8. Both non-minimal rows fail with
    `Assert.Throws() Failure: No exception was thrown`;
  - padding and message check: run and failed, 7 of 8, with the same rows;
  - current tree (`current.*`): run and passed, 8 of 8.

  The high-bit row passes on `a779602` in both variants (see
  `docs/header-length-validation.md`, Limits).
- `mutations/`, `mutations-command.txt`, `mutations-console.log`: the full Dafny
  mutation suite on `9673862` plus the uncommitted fixes, the same source as
  `34c2421`. Run and passed: baseline passed, 17 of 17 detected.
  `HEADER-001-short-sixteen-bit` took 8.8 s and failed at `Protocol.dfy(53,38)`,
  `126 <= length`. `HEADER-001-short-sixty-four-bit` took 9.0 s and failed at
  `(53,69)`, `length >= 65536`. In S2 they took 61.2 s and 85.2 s.
- `capture-34c2421.zip`, `capture-summary.md`, `capture-run.log`: the four
  `pdd.sh capture` jobs (protocol, tests, controlled, stryker), each into a new
  directory under `artifacts/pdd-review`, then `aggregate`. Tested commit
  `34c2421`, clean worktree (this folder was moved out while they ran).

## Checks at `34c2421`

| Check | Result |
| --- | --- |
| Pdd tests, `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release` | run and passed: 262 of 262 (in the protocol capture on `34c2421`, and before the commit on the working tree) |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| `capture --job protocol` | run and passed: manifest, fixtures (262 Pdd tests), proof `Dafny program verifier finished with 41 verified, 0 errors`, 17 of 17 mutants detected (header mutants 8.9 s and 9.6 s) |
| `capture --job tests` | run and passed: 269 of 269 tests on each of netstandard2.0, netstandard2.1, net8.0, net9.0 and net10.0 (all .NET 10.0.14 hosts); line coverage 83.1 % (70 % required) |
| `capture --job controlled` | run and passed |
| `capture --job stryker` | run and passed: recorded dispositions matched, `stryker-close-dispositions.json` unchanged, 5 min 56 s |
| `aggregate` | run and passed: machine evidence gate passed; review evidence not-run |
