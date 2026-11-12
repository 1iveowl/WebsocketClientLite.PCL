# Evidence: fixes for the `/code-review high` on #145 (M6, 2026-11-12)

The review posted 10 inline findings on #145 (comments 4545434880 to 4545435648). The
controller decision of 2026-11-12 on #145 governs: owner decision 6 still sets the scope
(`tools/Pdd/Mutations.cs`, `tools/Pdd/Requirements.cs` for the shared declaration lookup,
`tools/Pdd.Tests`, `verification/tests/fixtures`), budgets and `--cores` do not change, and
every existing mutant keeps its classification. Base `10d9e36`, the branch tip after S10.
The change is `5fd8632`. Everything here was made on 2026-11-12 (UTC). Each file carries its
own time.

**Load and the timing lock.** The #144 fix session ran on the same machine. Every timed run
(Pdd test runs, the mutation suite, the capture jobs) ran under `flock ~/worktrees/.timing.lock`
through `scripts/timed.sh`. Next to each log, `<name>.ps.txt` has the time the lock was taken,
`ps -eo pid,etime,comm` filtered to `claude` and `dotnet` (names only), and the time it was
released. `validate`, `dafny-masking.sh --check` and `aggregate` did not take the lock.

## What changed (`5fd8632`)

| Finding | Change |
| --- | --- |
| 1, 2 and 8 | `Requirements.BodyOpening` finds the body brace by the layout rule that `LogicalLine` and `Clauses` already rely on, not by guessing which braces are set displays. The body brace is on the signature (outside parentheses, brackets and attributes), or it is the first character of a line that continues neither the signature nor a clause. A clause is a line that starts with `requires`, `ensures`, `reads`, `modifies` or `decreases`, together with the lines indented more than its keyword and the lines inside a bracket or brace it opened. A brace in a contract is never the body. The lists of next-declaration keywords and operators are gone. |
| 3 | `NamedPredicateLines` keeps the qualifier of each call. A call qualified with another class or module, or a member call on a value, is skipped. Unqualified calls and calls qualified with the mutant's own class (`Protocol.F`, `ProtocolCore.Protocol.F`) count. |
| 4 | `Fixtures.Rendered(node, sources)` renders the anchors against the source each record is classified against: the mutated source for the observed runs in `payload-diagnostics.json`, the unchanged source for `mutation-cases.json`. (`Mutations.Apply` already only replaces one line with one line, so today the two renderings are equal.) |
| 5 | `Mutations.MethodKinds` and `FunctionKinds` are public, and `Fixtures` uses them. |
| 6 | `PayloadEvidenceTests` renders the diagnostics once (`Lazy`), and `Fixtures.BodyLine` caches per source and symbol. |
| 7 | `Requirements.DafnyClass` resolves the class once. `NamedPredicateLines` looks up every name in that scope, and `Classify` masks the source once and passes it to `Bounds` and `NamedPredicateLines`. |
| 9 | One test helper, `Fixtures.LineOf(source, text, exact, after)`, replaces the three line lookups. One production helper, `Requirements.LineAt`, replaces the repeated newline counts. |
| 10 | `Resolve` splits the symbol only on its C# path. |

## Failing first

`failing-first/`, run with `scripts/head-with-test.sh`. That script makes a `git archive` copy
of `10d9e36`, puts in one test file and runs the filtered Pdd tests. The test files are kept as
they were when they ran.

- `review-findings-1-3-on-10d9e36.*` with `ClassifierHardeningTests.at-run.cs`: run and failed,
  11 of 23. The 10 S10 tests and the two controls (`Protocol.` and `ProtocolCore.Protocol.`
  qualifiers) passed. Each failure is the defect the review named:
  - finding 1, the named predicate: after `reads *` followed by a `var` field or a `@`
    attribute, after `|[first]| == |[second]|` followed by a `least predicate`, and a body on
    the signature after `set<bool>` followed by a field. Each gave `inconclusive` where
    `detected` was expected: the real body was taken as a display and skipped;
  - finding 1, the mutated method: `decreases *` followed by a field threw
    `missing or ambiguous mutation method` out of `Apply`;
  - finding 2: a `match` in a contract, on one line and over several lines, gave
    `inconclusive` for the named predicate. For `DecodeHeader`, `Apply` threw
    `mutation must be a unique, changed single line in its method`, because the match arms
    were taken as the body (two rows);
  - finding 3: `Other.HeaderValid(` and `ProtocolCore.Other.HeaderValid(` gave `detected`,
    where `inconclusive` was expected.
- `review-finding-8-on-10d9e36.*` with `ClassifierHardeningTests.at-run-2.cs`: run and failed,
  1 of 1. If the body brace ends the last clause line, which is outside the layout rule, the
  old heuristic still found a body (`Assert.Throws() Failure: No exception was thrown`). Under
  the new rule, `Apply` throws and the run is inconclusive.
- The same tests on `5fd8632` (`after/review-findings-changed.*`): run and passed, 24 of 24.
  The final file uses `Fixtures.LineOf` instead of the local `Line` helper. The cases are
  the ones that ran.

## Every existing declaration keeps its body

`layout-probe/`: `Probe.cs` prints the body-brace line, found by `Requirements.DafnyMember`,
of every method, function, predicate and lemma in `Protocol.dfy` and `Masking.dfy`: 30
declarations, `NativeCopy.CopySlice` without a body. It ran against `git archive` copies of
`10d9e36` (the old heuristic) and `5fd8632` (the layout rule). `body-lines-10d9e36.txt` and
`body-lines-5fd8632.txt` are identical. So `Resolve`, `Bounds`, the named-predicate ranges and
the fixture anchors see the same lines as before on today's source.

## Checks on `5fd8632`, clean tree

From `scripts/m6-checks-1.sh` and `scripts/m6-checks-2.sh`, output in `after/`.

| Check | Result |
| --- | --- |
| Pdd tests | run and passed: 289 of 289 (275 at `10d9e36`, plus 14 rows in `ClassifierHardeningTests`) |
| Line shift (S10 finding 7) | run and passed: one comment line above `CloseStep` in a copy, 289 of 289 (`finding-7-shift-closestep.*`) |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| `scripts/dafny-masking.sh --check` | run and passed: `Dafny program verifier finished with 41 verified, 0 errors` |
| Mutation suite | run and passed: baseline passed, 17 of 17 detected, 8.5 to 9.0 s each. Status and matched clauses are identical to S10's baseline run on `29697a4` for every experiment (`classifications-vs-s10-baseline-suite.txt`) |
| `capture --job protocol` | run and passed: manifest, fixtures, proof, mutations (17 of 17 detected, 8.8 to 9.7 s, identical classifications: `classifications-vs-s10-baseline-protocol-capture.txt`) |
| `capture --job tests` | run and passed: restore, build, matrix, coverage run, coverage gate |
| Five-target matrix, Release | run and passed: 307 of 307 on each of netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 (.NET 10.0.14 hosts), in the tests capture |
| Coverage gate | run and passed: line coverage 83.1 % (70 % required), in the tests capture |
| `capture --job controlled` | run and passed: defect, boundaries, queued, leases, retention, reconnect |
| `capture --job stryker` | run and passed: runtime and stryker passed; `verification/stryker-close-dispositions.json` unchanged |
| `aggregate` | run and passed: machine evidence gate passed; review evidence not-run |

The capture set is in `after/capture-5fd8632.zip`, and the mutation suite's directory in
`after/mutations/`. `after/local-times.md` lists the per-mutant times next to S10's baseline. No
mutant took more than 30 s, a third of its 90 s budget.

## The timing lock leaked into build servers

`after/lock-leak-2026-11-12.txt`. A `flock(2)` lock belongs to the open file. The C# compiler
server (`VBCSCompiler`) and the MSBuild reuse nodes that a build starts under the lock inherit
the open lock file and outlive the command. After the controlled capture, the compiler server
held the lock for about five minutes. No timed run could start in that time, in this session
or any other, and the Stryker capture waited. I stopped the server (my own, started from this
worktree, while no build was running) and Stryker started in the same second. After the
Stryker capture, its MSBuild nodes and a new compiler server held the lock again, and I
stopped them too. No timed runs overlapped. The leak only delayed them. The lock helper is
outside the authorized files and is not changed. A proposed fix is in the note.

## CI

CI has to run on the branch's final commit, the commit that adds this folder. So its
run ID, conclusion and per-mutant times are not in this commit. They are in the M6 receipt
comment on #145, and no commit follows that run.
