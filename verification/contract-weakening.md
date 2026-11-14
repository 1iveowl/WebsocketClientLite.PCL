# Weakening the header length contract

Written 2026-11-14. This experiment asks what happens when the contract itself is wrong, not
the code. The verifier proves that the code meets its contract. If the contract asks for less
than RFC 6455 does, the verifier still says yes. Which of the other checks notices?

The contract is `HeaderValid` in `verification/Protocol.dfy` (line 47 at `d3308a0`). It holds
the length rule of RFC 6455 section 5.2: "the minimal number of bytes MUST be used to encode the
length". `DecodeHeader` (line 62) sets its status by calling `HeaderValid` (line 80), so
weakening `HeaderValid` weakens the code with it, and the decoder still meets its contract
(`ensures status == 2 ==> HeaderValid(...)`, line 68).

The weakened contract is never in the shipped code and is not part of the mutation catalog.
The runs below were made in a throwaway worktree.

## What was run

- **Commit:** `d3308a0` (the 10.4.0 release merge; `releases/10.5.0` differs from it only in
  documentation at the time of the runs).
- **Host:** Linux x86-64 dev container, kernel 7.2.11, .NET SDK 11.0.100, Dafny 4.11.0 with
  Z3 4.12.1 from `scripts/dafny-toolchain.sh`.
- **Script:** [`run.sh`](../docs/evidence/contract-weakening/run.sh) runs the two cases and a
  clean mutation baseline, then [`quiet-rerun.sh`](../docs/evidence/contract-weakening/quiet-rerun.sh)
  repeats one mutation run and applies one mutant by hand. Both take a throwaway worktree made
  with `git worktree add --detach <path> d3308a0` and an empty output directory.

The two patches change line 53, one bound each:

| Case | Line 53 at `d3308a0` | Weakened | Patch |
| --- | --- | --- | --- |
| Marker 126 (16-bit length) | `126 <= length <= 65535` | `0 <= length <= 65535` | [`marker126.patch`](../docs/evidence/contract-weakening/marker126.patch) |
| Marker 127 (64-bit length) | `length >= 65536` | `length >= 0` | [`marker127.patch`](../docs/evidence/contract-weakening/marker127.patch) |

For each case, in order:

```bash
git apply marker126.patch                     # or marker127.patch
bash scripts/dafny-masking.sh --check         # a) and b)
bash scripts/dafny-masking.sh --write         # regenerate the C# from the weakened contract
./test-all-tfms.sh Release                    # c)
./scripts/pdd.sh mutations --output <dir>     # d)
git checkout -- verification/
```

## Results

The clean baseline at `d3308a0`: `--check` passes with `Dafny program verifier finished with 41
verified, 0 errors`; `./test-all-tfms.sh Release` passes 328 tests on each of the six targets;
`./scripts/pdd.sh mutations` passes the baseline and detects all 17 mutants
([`baseline-check.txt`](../docs/evidence/contract-weakening/baseline-check.txt),
[`baseline-tests.txt`](../docs/evidence/contract-weakening/baseline-tests.txt),
[`baseline-mutations.txt`](../docs/evidence/contract-weakening/baseline-mutations.txt)).

| Check | Marker 126 weakened | Marker 127 weakened |
| --- | --- | --- |
| a) Verifier's last line | `Dafny program verifier finished with 41 verified, 0 errors` | `Dafny program verifier finished with 41 verified, 0 errors` |
| b) `dafny-masking.sh --check` | exit 1: the generated C# differs from `Masking.g.cs` | exit 1: the same |
| c) Tests after regenerating | 2 of 328 fail on each of the six targets, 12 in all | 2 of 328 fail on each of the six targets, 12 in all |
| d) Header mutants | `HEADER-001-short-sixteen-bit` survives; the run stops there | `HEADER-001-short-sixteen-bit` detected; `HEADER-001-short-sixty-four-bit` survives |

### a) The verifier

With either bound weakened, every proof still holds. `DecodeHeader` calls the weakened
`HeaderValid`, so it accepts exactly what the weakened contract allows, and `HeaderRoundTrip`
still decodes every header the encoder writes. The verifier checks the code against the
contract; nothing in it compares the contract with the RFC.

### b) The generated-code check

`scripts/dafny-masking.sh --check` fails, but not because it finds the bug. It regenerates the C#
and compares it with the checked-in `verification/generated/Masking.g.cs`. `HeaderValid` is
compiled into that file, so any edit to it that is not regenerated fails the check: a correct
edit as much as a wrong one. After `--write` the check passes with the weakened contract. It
shows that the C# is current, not that the contract is right. The diffs are in
[`marker126-2-generated.diff.txt`](../docs/evidence/contract-weakening/marker126-2-generated.diff.txt)
and [`marker127-2-generated.diff.txt`](../docs/evidence/contract-weakening/marker127-2-generated.diff.txt):
line 114, in the copy of the Dafny source the file carries, and line 705, in the compiled
`HeaderValid`.

### c) The tests

After the C# is regenerated from the weakened contract, the tests built from the RFC catch both
cases. On each of the six targets (`netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`, `net11.0`), exactly two tests fail and the other 326 pass:

| Case | Failing test | Row |
| --- | --- | --- |
| Marker 126 | `PayloadParsingTests.RejectsMalformedFrames` | `[129, 126, 0, 125]`: 125 in the 16-bit field |
| Marker 126 | `ParserRetentionTests.InvalidHeadersRejectBeforePayloadRead` | `[129, 126, 0, 125]`, limit 0, read 4 |
| Marker 127 | `PayloadParsingTests.RejectsMalformedFrames` | `[129, 127, 0, 0, 0, 0, 0, 0, 255, 255]`: 65535 in the 64-bit field |
| Marker 127 | `ParserRetentionTests.InvalidHeadersRejectBeforePayloadRead` | the same row, limit 0, read 10 |

The test output shortens the 64-bit row to `[129, 127, 0, 0, 0, ···]`. It is the row at
`PayloadParsingTests.cs` line 242: the only other row that starts `0x81, 127`, the bit-63 row at
line 246, starts `129, 127, 128` and still passes. Both tests read the same row list,
`PayloadParsingTests.InvalidHeaders`. No other test fails in either case
([`marker126-3-tests.txt`](../docs/evidence/contract-weakening/marker126-3-tests.txt),
[`marker127-3-tests.txt`](../docs/evidence/contract-weakening/marker127-3-tests.txt)).

Each weakening is caught by one row of that list. Remove the row and no test catches it.

### d) The mutation suite

Both header mutants add one wrong acceptance to `DecodeHeader`: a 125-byte length in the 16-bit
field, or a 65535-byte length in the 64-bit field. Against the real contract the verifier rejects
both. Against a contract weakened in the same place, the extra acceptance is already allowed, so
the mutant changes nothing the contract forbids and survives. The mutation suite therefore fails
(exit 1) with the weakened contract in place, regenerated or not, because a mutant written for
the strict rule no longer breaks it.

The runner stops at the first mutant that is not detected (`tools/Pdd/Mutations.cs`), so in the
marker 126 run `HEADER-001-short-sixty-four-bit` is recorded as `not-run`
([`marker126-mutations-evidence/results.json`](../docs/evidence/contract-weakening/marker126-mutations-evidence/results.json)).
To answer for it, `quiet-rerun.sh` applies that mutant by hand on top of the weakened 16-bit
contract and verifies with the runner's command: `Dafny program verifier finished with 573
verified, 1 error`, the postcondition at line 68 failing on the untouched 64-bit bound at line 53
([`marker126-6-sixty-four-bit-mutant.txt`](../docs/evidence/contract-weakening/marker126-6-sixty-four-bit-mutant.txt)).
That run is not classified by the tool; it is the verifier's own output. (`dafny verify` counts
assertion batches, so its 573 is not comparable with the 41 that `dafny translate` reports.)

| Mutant | Real contract | Marker 126 weakened | Marker 127 weakened |
| --- | --- | --- | --- |
| `HEADER-001-short-sixteen-bit` | detected | **survived** | detected |
| `HEADER-001-short-sixty-four-bit` | detected | not run by the suite; caught when run by hand | **survived** |

The other 15 mutants are detected in every run.

## What this shows

- The proof shows that the code meets the contract, and nothing more. Two weakened bounds pass
  the verifier with 0 errors.
- `--check` fails on the change but does not detect the bug: it fails on any edit that is not
  regenerated, and passes once the C# is regenerated.
- The tests catch both weakenings, because their expected results come from the RFC, not from
  the code. Each is caught by a single row.
- The mutation suite fails too, for a reason that is easy to misread: a mutant that the real
  contract catches survives under the weakened one. That signal is only as good as the catalog.
  A mutant that "survives" after a contract change says the contract moved, and the answer is to
  review the contract, not to drop the mutant.
- No open gap was found for these two bounds. Both are caught by the tests and by the mutation
  suite.

## Limits

- One clause, two bounds. Other clauses of `HeaderValid`, such as the bit-63 rule, the control
  frame limit or the size limit, were not weakened here.
- The pull request check that lists contract changes for review (`contract-change-report` in
  `.github/workflows/ci.yml`) was not run in this experiment.
- Timings: the first marker 126 mutation run shared the machine with other builds, and several
  mutants took 37 to 77 s instead of about 9 s. `quiet-rerun.sh` repeated it with nothing else
  from this work running, and still saw 39 s for the baseline and 76 s for
  `MASK-002-corrupt-bulk-byte`; the budget is 90 s. The outcomes were the same in both runs. Both
  runs are kept ([`marker126-4-mutations.txt`](../docs/evidence/contract-weakening/marker126-4-mutations.txt),
  [`marker126-5-mutations-quiet.txt`](../docs/evidence/contract-weakening/marker126-5-mutations-quiet.txt)).
- After every case the worktree was restored; `final-status.txt` and `final-status-2.txt` are the
  empty `git status --short` after each script.
