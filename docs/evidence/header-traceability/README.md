# Header traceability, step S2 (2026-11-11)

Evidence for step S2 of `docs/pdd-10.3-session-plan.md` on
`chore/header-rule-traceability` (#136). Everything here was made on 2026-11-11.

The step stopped once at item 3: in the first suite run neither header mutant
was detected at its named clause. The owner then answered Q1 to Q3 on #136
(retarget the mutants and extend the classifier; follow the fixtures to the
moved line numbers; let test references carry array arguments). The change was
committed as `911f2d4`, and the capture jobs ran on that commit with a clean
worktree.

## Files

- `baseline/`: `pdd.sh validate` (passed) and the Pdd tests (11 of 258 failed) at
  `ba499c3` before any S2 edit, and the Pdd tests at `28e30cb`, before S1
  (258 passed), run in a separate worktree. The 11 failures came from fixtures
  that pinned line numbers which S1's comments moved.
- `mutations/`: suite run 1, before the owner's answers, on an interim working
  tree that left out two test references (`mutations-command.txt`,
  `mutations-console.log`). Baseline passed and the 15 existing mutants were
  detected. `HEADER-001-short-sixteen-bit` was inconclusive, so the runner
  stopped and `HEADER-001-short-sixty-four-bit` was not-run. Kept as a failed run.
- `diagnostic-sixty-four-bit/`: one diagnostic run of the second header mutant
  outside the suite, same Dafny command, on a scratch copy. It is not a suite
  result. Attempt 1 failed to launch (`/usr/bin/time` missing) and is kept.
  Attempt 2 completed; its elapsed time was not recorded (`bc` missing).
- `dafny-masking-check.log`: `scripts/dafny-masking.sh --check` on the
  working tree before the answers, passed.
- `pdd-tests-interim.log`: the Pdd tests on the working tree before the answers,
  12 of 258 failed.
- `mutations-2/`: suite run 2, after the answers, on the tree committed as
  `911f2d4`. Baseline passed and all 17 mutants were detected. Its output is
  the source of `verification/tests/fixtures/payload-diagnostics.json`.
- `pdd-tests.log`: the Pdd tests on that tree, 262 of 262 passed.
- `capture-911f2d4.zip`, `capture-summary.md` and `capture-run.log`: the four
  `pdd.sh capture` jobs (protocol, tests, controlled, stryker), each into a new
  directory under `artifacts/pdd-s2`, then `aggregate`. Tested commit
  `911f2d4`, dirty worktree false. The machine gate passed. Review evidence is
  not-run. This evidence commit is not the tested commit.

## Outcome of the header mutants

Run 1 named the `status == 2 <==> HeaderValid(...)` part of the postcondition on
lines 69 to 71 as the target. Both mutants give exit 4 and `573 verified, 1 error`.
The only error is the postcondition
`ensures status == 2 ==> HeaderValid(first, second, length, limit) && available >= headerSize - 2`
(line 68), with a second related location, "this proposition could not be
proved", inside `HeaderValid` (line 53). The biconditional is not reported. A
likely reason, not verified: the verifier assumes a failed postcondition when it
checks later ones, and with line 68 assumed the biconditional holds for a mutant
that only accepts more headers.

In run 2, after the owner's answer to Q1, the target is the line-68 clause, and
the classifier accepts the second location because it lies inside a predicate
named in that clause. Both mutants are detected, in 61.2 and 85.2 seconds
against a 90-second budget. The protocol capture gives 61.2 and 85.2 seconds
too. The other mutants take about 9 seconds each.

## F: proved contracts, citations, requirements and mutants

"Mutants" counts catalog entries in `verification/mutations/` and their result in
suite run 2.

| File | Declaration | RFC 6455 section or policy (source comment) | Requirement | Mutants |
| --- | --- | --- | --- | --- |
| Protocol.dfy | `ReadLength` | 5.2 | HEADER-001 (new) | none |
| Protocol.dfy | `DecodeHeader` | 5.2, with HeaderValid's policy | HEADER-001 (new) | 2, detected |
| Protocol.dfy | `Step` | 5.4, 5.5, 5.5.1 and library policy | none | none |
| Protocol.dfy | `Admit` | 5.2, 5.4, 5.5 and library policy | HEADER-002 (new) | none |
| Protocol.dfy | `SendStep` | 5.4, 5.5, 5.5.1 and library policy | none | none |
| Protocol.dfy | `CloseStep` | 5.5.1, 7.1.2 and library policy | CLOSE-001, 002, 003 | 5, detected |
| Protocol.dfy | `Merge` | 5.4 and library policy | REASSEMBLY-001 | 2, detected |
| Protocol.dfy | `MergeScalar` | 5.4 and library policy | REASSEMBLY-002 | 1, detected |
| Protocol.dfy | `PartsPrefix` (lemma) | 5.4 through Parts, proof helper | none | none |
| Protocol.dfy | `WordSnoc` (lemma) | 5.2 through Word, proof helper | none | none |
| Protocol.dfy | `NetworkWord` (lemma) | 5.2, proof helper | none | none |
| Protocol.dfy | `HeaderRoundTrip` | 5.2, 5.3 and library policy | none | none |
| Protocol.dfy | `Outgoing` | 5.2, 5.3, 5.4, 5.5 and library policy | none | none |
| Protocol.dfy | `WriteHeader` | 5.2, 5.3 | none | none |
| Protocol.dfy | `CopyMasked` | 5.3 | MASK-002 | 3, detected |
| Protocol.dfy | `MaskSlice` | 5.3 and library policy | MASK-003 | 2, detected |
| Protocol.dfy | `NativeCopy.CopySlice` (extern) | Library policy, trusted | REASSEMBLY-003 maps no contract; NATIVE-COPY boundary | none, excluded |
| Masking.dfy | `Apply` | 5.3 | MASK-001 | 2, detected |
| Masking.dfy | `XorTwice` (lemma) | none in source | none | none |
| Masking.dfy | `RoundTrip` (lemma) | none in source | none | none |

For `XorTwice` and `RoundTrip`, section 5.3 says "the same steps are applied to
mask the data as to unmask the data". That is a proposed citation; it was not
added, because S2 does not edit `Masking.dfy`.

Specification functions, which carry no contract of their own: `Word` (5.2),
`Known` (5.2), `PrefixValid` and `HeaderValid` (5.1, 5.2, 5.5 and policy), `Parts`
(5.4), `TxState` (policy), `SendFailed` (policy), `CloseCode` (7.4.1, 7.4.2 and
policy), `CloseValid` (5.5, 5.5.1, 7.4, 8.1), `HandshakeValid` (4.1 and policy).
None is a mutation target. `CloseValid` (which uses `CloseCode`) and
`HandshakeValid` are called from C# but have no requirement entry.
