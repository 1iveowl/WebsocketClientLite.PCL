# Evidence: recount at the head of `releases/10.5.0` (2026-11-14)

Step R of `docs/pdd-10.5-plan.md`. Every value below was read at
`e752ffcd62000e51264ec81771ebf1a9014f8b0e` (#170), the head of `releases/10.5.0` once #164 to
#170 had merged, in a clean worktree made with `git worktree add --detach <path>
origin/releases/10.5.0`. `final-status.txt` is the empty `git status --short` after step 4
(Stryker); after the controlled job the receipt records `dirty_worktree: false`
(`receipt.json`, `jobs.controlled.source_after`, in the zip). The checks ran one after another, with nothing else running on the host, from 16:08 to
16:23 UTC. Nothing was repeated; no check failed or was interrupted. The pull request that adds
this folder changes nothing else, so the values hold for its merge commit too.

Host: Linux x86-64 dev container, kernel 7.2.11, .NET SDK 11.0.100, Dafny 4.11.0 with Z3 4.12.1.

## Files

| File | What it is |
| --- | --- |
| [`scripts/recount.sh`](scripts/recount.sh) | The driver: steps 0 to 4 in order, each into its own output file |
| [`scripts/static-recount.py`](scripts/static-recount.py) | Step 0: counts and line numbers read from the tree |
| `0-static.txt` to `4-stryker.txt` | The output of each step: `static-recount.py`, `dafny-masking.sh --check`, and `./scripts/pdd.sh capture --job protocol`, `--job tests`, `--job stryker` |
| `5-controlled.txt`, `6-aggregate.txt` | Run by hand after the driver: `capture --job controlled --command-budget 480`, then `./scripts/pdd.sh aggregate` over all four jobs. Their `$` header lines were written with `<out>`, and `6-aggregate.txt` has no start or finish time. `recount.sh` now runs both as steps 5 and 6 |
| `summary.md` | The aggregated receipt's summary: machine evidence gate **passed** |
| `protocol-proof.txt`, `protocol-mutations.txt`, `tests-matrix.txt`, `tests-coverage-gate.txt`, `stryker.txt` | The jobs' own logs, copied from the capture as text |
| `capture-e752ffc.zip` | The output directory as captured, except the `DONE` marker: 446 entries (348 files), including `receipt.json`, every TRX, the coverage report, the Stryker JSON and HTML reports and the mutation sources |

In the text files, paths were replaced after capture: `<out>` for the output directory,
`<worktree>` for the worktree, `<scratch>` for the rest of the scratch folder and `~` for the
home directory. The four lines of `stryker.txt` that held the wrapped report path read `<path
replaced: …>`. The zip is unchanged, so the hashes in its `receipt.json` still match its files.

## The recount

| Value | Result | Source |
| --- | --- | --- |
| Verifier's last line | `Dafny program verifier finished with 41 verified, 0 errors` | `1-check.txt` line 19; `protocol-proof.txt` line 17 |
| Dafny lines | `verification/Protocol.dfy` 574, `verification/Masking.dfy` 45 | `0-static.txt` lines 3 and 4 |
| Generated C# lines | `verification/generated/Masking.g.cs` 1129 | `0-static.txt` line 5 |
| Library C# lines | `src/main/WebsocketClientLite` 2853 in 25 files; `src/interface/IWebsocketClientLite` 591 in 8 files | `0-static.txt` lines 6 and 7 |
| Library test lines | `src/main/WebsocketClientLiteTest` 4483 in 26 files | `0-static.txt` line 8 |
| `tools/Pdd` lines | 2658 in 17 files; `tools/Pdd.Tests` 2027 in 16 files | `0-static.txt` lines 9 and 10 |
| Requirements | 16 | `0-static.txt` line 11 (`verification/requirements.json`) |
| Obligations by kind | 53: test 18, review 16, proof 10, mutation 9; all `existing` | `0-static.txt` lines 12 to 14 |
| Boundary entries | 12 | `0-static.txt` line 15 (`verification/boundaries.json`) |
| Dafny mutants | 17 in the catalogs, 17 detected, 8.456 to 9.394 s each; baseline 9.281 s; budget 90 s per mutant, 180 s for the baseline | `0-static.txt` line 16; `protocol-mutations.txt` lines 7 to 24; budgets in `receipt.json`, `dafny_mutations.experiments[].budget_seconds` |
| Tests per target | 328 passed, 0 failed, on each of 6 targets (`netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`, `net11.0`), all on a .NET 11.0.0 host | `tests-matrix.txt` lines 19, 39, 59, 79, 99 and 119; `summary.md` |
| Line coverage | 84.6 % (branch 80.3 %), against a 70 % minimum | `tests-coverage-gate.txt` lines 1 to 4 |
| Stryker on `Service/WebsocketConnectionHandler.cs`, the Close and handshake file | 105 mutants: 42 killed, 27 survived, 7 no coverage, 3 compile errors, 26 ignored, 0 timeouts; score 55.26 %. The other files appear only as ignored or compile-error outcomes | `stryker/reports/reports/mutation-report.json` in the zip; `stryker.txt` line 50 |
| Stryker must-kill | 16 | `verification/stryker-close-dispositions.json` (`required_killed`, line 3); `0-static.txt` line 17 |
| Stryker dispositions | 54: 27 out of scope, 20 accepted gaps, 7 equivalent | `verification/stryker-close-dispositions.json`; `0-static.txt` line 18 |
| Evidence gate | passed. `summary.md` line 5: "Machine obligations, controlled ordering and boundary experiments, recorded Stryker dispositions and existing CI gates; PR review remains separate and unsatisfied." The must-kill set is part of the Stryker job, which fails if any of the 16 is not killed (`tools/Pdd/StrykerEvidence.cs` lines 50 and 51) | `summary.md` lines 3 and 5; `6-aggregate.txt` line 12; `4-stryker.txt` line 10 |
| `HeaderValid` RFC citation | `verification/Protocol.dfy` line 39 | `0-static.txt` line 19 |
| `HeaderValid` | `verification/Protocol.dfy` line 47 | `0-static.txt` line 20 |
| The length rule | `verification/Protocol.dfy` line 53 | `0-static.txt` line 21 |
| `DecodeHeader` | `verification/Protocol.dfy` line 62 | `0-static.txt` line 22 |
| The decoder's `status == 2` clause | `verification/Protocol.dfy` line 68 | `0-static.txt` line 23 |
| `HeaderRoundTrip` | `verification/Protocol.dfy` line 353 | `0-static.txt` line 24 |
| `Outgoing` | `verification/Protocol.dfy` line 426 | `0-static.txt` line 25 |
| The `81 7E 00 7D` row | `src/main/WebsocketClientLiteTest/PayloadParsingTests.cs` line 241 | `0-static.txt` line 26 |
| `RejectsMalformedFrames` | `PayloadParsingTests.cs` line 261 | `0-static.txt` line 27 |
| Production call to the decoder | `src/main/WebsocketClientLite/Helper/DataframeParsing.cs` lines 41 and 48 call `Protocol.Admit`, which calls `DecodeHeader` at `Protocol.dfy` line 147 | `0-static.txt` lines 28 and 29 |
| The sentence on stricter validation | Not in the README since #158 moved the version log out of it: `CHANGELOG.md` line 82 (the 10.0.0 section) and `docs/protocol-verification.md` line 161 | `0-static.txt` line 30 |
| The sentence on executable specifications | `verification/README.md` line 251 | `0-static.txt` line 31 |
| `HeaderValid`'s body unchanged since `8f8f207` | yes | `0-static.txt` line 32 |

No line of `Protocol.dfy` moved in 10.5: nothing was added to it. `Masking.dfy` grew by three
comment lines (#169).

## Limits

- This is a local recount, not GitHub CI. Pull requests into `releases/10.5.0` run only the CI
  scope job; the release pull request into `main` runs the full workflow.
- The gate's own words apply: "PR review remains separate and unsatisfied". Each pull request was
  reviewed on GitHub; the receipt does not record that.
