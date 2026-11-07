# PDD phase 2 plan for 10.2.0.0

Make the existing protocol proofs traceable to requirements and make their evidence reproducible. Start with Close handling, then extend the same workflow to reassembly and masking. Keep Dafny as the verifier and C# as the asynchronous integration layer.

Status: proposed implementation plan. Target release branch: `releases/10.2.0.0`. Baseline inspected: `81859273eab73940dc8453df9f076816573f1d03`. This document adds no runtime behavior or new verification claims.

## Inspiration and existing foundation

The inspiration is Microsoft Research's [Formally verifying agent-written full-stack web apps with PDD Kit](https://www.microsoft.com/en-us/research/blog/formally-verifying-agent-written-full-stack-web-apps-with-pdd-kit/), by Alex Lavaee and Matthai Philipose, published November 5, 2026. The article describes intent artifacts, explicit trusted boundaries, mutation checks against contracts, and verification receipts. Its PDD Kit prototype uses Rust and Verus; this plan adapts the workflow to our existing Dafny/C# implementation rather than adopting that toolchain.

The [original adoption plan](dafny-pdd-plan.md) introduced the verified core. The [current verification guide](../verification/README.md) and [proof-boundary explanation](protocol-verification.md) describe what is proved, assumed and tested. Existing CI verifies the Dafny sources, audits explicit trusted contracts, checks generated C# for drift, builds all library targets, runs the target matrix and enforces 70% line coverage. Negative proof experiments are documented, but are not yet a repeatable mutation suite.

Phase 2 connects those checks to stable requirements. A manifest validator checks structure and references; it cannot establish that a requirement expresses the right intent. A receipt records evidence; it does not enlarge the scope of a proof.

## Scope and working rules

- Retain the current pinned verifier, generated-code integration and supported target frameworks.
- Give each requirement an explicit proof, test or review obligation. A requirement may need all three, with separate results.
- State both prohibited behavior and required successful behavior where applicable. Rejecting every input must not satisfy an acceptance requirement.
- Record preconditions, external assumptions and exclusions before implementing a new guarantee.
- Review changes to intent, stronger preconditions, weaker postconditions and expanded trust explicitly. Do not alter a contract merely to make verification pass.
- Keep adapter, independent-byte-vector and integration tests even where a core algorithm is proved. Removing a test requires identifying which remaining evidence covers its boundary.
- Do not claim full RFC conformance, proof of generated C#, exhaustive concurrency verification or unconditional termination.
- Keep `GitVersion.yml` unchanged. Publication remains the owner's release step.

## 1. Define the Close requirement map

Add `verification/requirements.json` with a versioned schema and a small validator (originally Python in A; migrated to the standalone .NET tool in D2). Keep the first manifest limited to Close handling. Use stable IDs rather than source line numbers as identities.

Each entry records:

| Field | Meaning |
| --- | --- |
| ID and intent | Stable ID and a plain-language behavioral requirement |
| Origin | RFC section link or an explicitly identified library policy |
| Contract | Dafny file, symbol and the relevant clause or stable clause marker |
| Production path | C# caller and the responsibility it must discharge |
| Assumptions | References into the boundary register |
| Evidence obligations | Required proof checks, named tests, mutations and review items |
| Exclusions | Behavior this entry does not promise |

Keep observed run results out of the manifest; they belong in receipts. Support tested-only requirements without pretending they have a Dafny proof. Reject duplicate IDs, dangling boundary references, missing files and incomplete required fields. Resolve symbols and test identities through a documented mechanism and reject unresolved references rather than silently accepting them.

Initial Close requirements should distinguish these obligations:

| Proposed ID | Intent | Evidence and limit |
| --- | --- | --- |
| `CLOSE-001` | Reserve at most one Close send attempt; issue the send decision when a send request is eligible. | `CloseStep` contracts plus mutation checks. A reservation does not establish a completed wire write. |
| `CLOSE-002` | Preserve previously set closing-state bits. | `CloseStep` monotonicity contract and a targeted mutation. |
| `CLOSE-003` | Return the release decision exactly when the modeled state permits it: both directions recorded or terminal timeout/failure. | `CloseStep` contract. The sent bit represents reservation, so wire-write ordering is a separate obligation. |
| `CLOSE-004` | On the graceful path, finish the Close write before releasing the transport. | C# caller review and controlled integration tests. Record failure/abort exceptions explicitly. |
| `CLOSE-005` | Peer response or the configured timeout permits asynchronous disposal to finish under the stated stream and scheduling assumptions. | Controlled tests and boundary review. This is not an unconditional liveness proof. |

Review the precise wording against the actual callers and applicable RFC sections before freezing these entries. Record any ambiguity as a decision with alternatives and the chosen outcome; do not infer a new public API promise from an existing test name.

Exit gate: every initial requirement has an identified production path and evidence obligation, and malformed-manifest fixtures demonstrate the validator's failure behavior. A reviewer can distinguish each sequential guarantee from its asynchronous boundary.

## 2. Automate negative proof checks

Add a mutation catalog and runner under `verification/mutations/` and `scripts/`. Begin with the documented premature Close-release experiment, then add duplicate-send and state-bit-loss mutants. Extend later to the documented reassembly-offset and XOR-to-OR examples.

For each mutant, record its requirement ID, exact edit, expected target obligation and diagnostic classification. Apply one mutation at a time in a temporary source tree. Never edit the working checkout, checked-in generated C# or published packages. Verify the unchanged baseline first using the pinned toolchain.

A mutant is detected only when verification rejects the targeted semantic obligation. Parse errors, failed mutation application, missing tools, unrelated failures and timeouts are inconclusive failures of the experiment, not successful detection. A mutant that verifies is a gap to investigate; it may be equivalent under the preconditions, or the contract may be too weak. Document equivalent mutants rather than weakening the gate to count them as detected.

Maintain at least one meaningful mutant for each selected behavioral obligation, with explicit reviewed exceptions. Mutation coverage is not proof that the specification is complete. Record stdout/stderr, exit status, source hashes, tool versions and the obligation matched for each experiment.

Exit gate: baseline passes; the Close mutants fail at their intended obligations; runner checks distinguish surviving mutants and infrastructure failures; cleanup preserves the original sources even after interruption. Set execution budgets from measured CI runs and make budget exhaustion visible.

## 3. Record boundary assumptions and contract changes

Add `verification/boundaries.json` for responsibilities outside the Dafny proof. Keep the existing `trusted-boundaries.md` as the machine-compared Dafny audit report; do not mix handwritten entries into that generated format.

Each boundary entry names the assumption, implementation owner by code path, dependent requirement IDs, test obligations and known exclusions. Begin with Close state-lock ownership, send reservation versus write completion, transport failure, timeout delivery and observer/disposal interactions. Later include buffer ownership, native copy, retained-fragment totals, UTF-8 and handshake parsing.

Add a PR comparison report against the actual target branch's merge base. Surface changes to requirement intent, `requires`/`ensures`, supporting specification definitions, external contracts and boundary entries. Include source diffs so changes outside the extracted summary remain reviewable. Flag changes as requiring semantic review; do not claim a textual comparison can decide logical equivalence or detect every weakening.

The review record should identify the accepted decision and revision through the normal PR review mechanism. A committed boolean such as `approved: true` is not approval evidence. Agent gap review may assist, but cannot substitute for verifier results or owner decisions about changed guarantees.

Exit gate: representative changes to preconditions, postconditions, specification helpers and trust entries appear in the report. The existing external-contract audit remains enforced. Document the comparison tool's limitations.

## 4. Produce verification receipts in CI

Add a receipt generator that consumes actual verifier, mutation and test outputs. Produce `artifacts/pdd/receipt.json` and a short Markdown summary for the CI job/PR. Do not require committing generated run evidence to the repository.

Record the checked-out commit, dirty-worktree indicator, relevant source/manifest hashes, tool and SDK versions, commands, exit codes, timestamps, CI run identity, library target and actual host runtime. Distinguish a PR's tested merge commit from its source head where applicable. Existing matrix runs use a .NET 10 test host; five library targets must not be reported as five independently tested runtimes.

For every obligation use explicit `passed`, `failed`, `skipped` or `not-run` status, with links to logs and test results. Record review evidence separately from machine checks. Only aggregate a requirement as satisfied when all its required evidence is present for the same source revision. A successful proof cannot hide a missing boundary test.

Combine outputs from the existing independent CI jobs in a final evidence job. Validate matching source identities before combining them. Upload partial evidence on failure and cancellation where the CI runner permits it; missing output must never become a passing receipt. Use fresh output directories to avoid stale test and coverage reports.

Exit gate: successful and deliberately failed fixture runs produce accurate receipts; mismatched revisions and missing required results fail the evidence gate. Existing verification, target-matrix and coverage checks remain required. Receipts are traceability artifacts, not signed attestations.

## 5. Exercise asynchronous boundaries with controlled schedules

Use the [Stryker.NET Close boundary pilot](../verification/stryker-close-pilot.md) to challenge the handwritten C# tests in this increment. Record its JSON results separately from Dafny mutations in the D/E receipts, triage relevant surviving and uncovered mutants, and set a CI budget and gate from the measured results. A recorded run and dispositions for those mutants are part of this increment's exit gate; Stryker does not replace the controlled schedules below.

Extend the test harness with explicit stream pause/resume points and, where needed, a controllable timeout source. Prefer existing injected delegates and internal seams; avoid changing the public API just for tests. Keep real loopback tests alongside controlled tests.

Specify each schedule before implementing its test:

1. Pause a Close write, initiate disposal and verify graceful transport release waits for write completion.
2. Race peer Close with local Close/disposal and verify only one Close attempt is reserved and the observed wire behavior matches the selected failure assumptions.
3. Trigger timeout or transport failure while waiting for peer Close and verify the documented terminal path.
4. Extend to cancellation between fragments, a queued Pong after peer Close, and a send attempted after write failure.

Use synchronization barriers to establish ordering, not delays to hope for it. Give every wait a diagnostic timeout and ensure cleanup cannot leave a hung test. Record the sequence of controlled events on failure. Explicit schedules cover selected interleavings only; they are not exhaustive scheduler exploration.

Exit gate: Close boundary tests reproducibly reach the intended interleavings, detect a deliberately introduced ordering defect in an isolated experiment, and pass across the supported library-target matrix. Any runtime correction discovered here gets its own scoped fix and evidence.

## Delivery order and completion

Implement one topic branch/PR per increment from the selected release branch:

| Increment | Deliverable | Depends on |
| --- | --- | --- |
| A | Close manifest, schema/validator and reviewed intent | This plan |
| B | Close mutation catalog and isolated runner | A |
| C | Boundary register and contract-change report | A |
| D | CI receipt generation and evidence gate | B, C |
| D2 | Standalone .NET PDD tooling and compatibility evidence | D |
| E | Controlled Close schedules and updated evidence | C, D, D2 |
| F | Extend the established workflow to reassembly and masking | E |
| G | Close evidence reliability and remaining boundary tests | D2, E, F |

### D2: consolidate repository tooling in C#

Increment D merged into `releases/10.2.0.0` in PR #108 (`a51f52f`). D2 migrates
its evidence pipeline and the A/B/C tooling to `tools/Pdd`, a standalone .NET 10
console project with one separate test project. It is independent of the
production solution and is never a published-package dependency. Commands,
compatibility findings and future-increment usage are documented in the
[tooling README](../tools/Pdd/README.md).

Keep D2 a small repository tool, not a general-purpose PDD framework. The prior
Python implementation's approximately 840 lines plus 610 test lines are a scope
reference, not a strict C# line limit. Explicit types can increase size, but every
abstraction needs a concrete current use. Use straightforward commands, shared
data types, a small process runner and standard .NET facilities. Do not introduce
plugin systems, generic workflow engines, dependency-injection frameworks,
speculative extensibility, extra architectural layers or additional projects
without a demonstrated need. Do not build a general JSON Schema engine or
language parser, or turn each operation into an interface/service. Keep code
clear; do not remove validation, cancellation or evidence checks to reduce size.
Report implementation/test line counts, project count and new dependencies at
D2 delivery, explaining substantial growth.

D2 preserves existing JSON formats and requirement identities, source matching,
explicit review requirements, pinned Dafny/Z3, generated C# bytes, targeted
mutation classification, the five-library-target matrix and actual host runtime.
Missing, malformed, interrupted or stale evidence must fail closed. Compatibility
fixtures preserve Python reference outcomes; intentional safety corrections and
diff-format changes are documented rather than silently hidden in the port.

The exit gate includes tooling failure-mode tests and cross-language fixture
comparisons, real timeout/SIGTERM descendant cleanup, the pinned baseline plus
all five Close mutants, generated-source consistency, Release matrix and 70%
coverage. CI explicitly installs .NET 10 in verification, comparison and
aggregation jobs. Migrated Python implementations and tests are removed after
comparison; no PDD execution path requires Python.

E uses `./scripts/pdd.sh capture --job protocol`, `capture --job tests` and
`aggregate` with fresh job directories. E's controlled schedules and separate
Stryker results remain pending, as does revision-specific PR acceptance. D2 does
not satisfy those obligations or turn a machine receipt into review evidence.
F extends these same focused commands for reassembly/masking with their boundary
obligations intact.

For F, map `Merge` and `MergeScalar` separately, preserving the production native-copy assumption. Map masking's XOR and preservation guarantees to independent expected-byte tests and the mutation experiment. Do not remove those integration checks because the Dafny algorithm has a proof.

Phase 2 is complete when Close, reassembly and masking have reviewed requirement maps, explicit boundary obligations, repeatable targeted mutations and revision-specific CI receipts. Close must additionally have controlled asynchronous boundary tests. The verification guide should link the resulting commands and explain any remaining evidence gaps.

For documentation-only increments, validate links and diffs. For tooling changes, test meaningful failure modes as well as the happy path. For runtime/test integration changes, run the Release solution build, `./test-all-tfms.sh Release`, the existing 70% coverage gate and `./scripts/dafny-masking.sh --check`. Run the new PDD checks for every affected requirement. Record measured CI cost before deciding whether the expanded mutation suite needs a separate scheduled run; the small Close suite remains a PR gate.

Defer introducing another verifier, proving the complete asynchronous client, exhaustive schedule exploration, a general-purpose PDD framework and automated test pruning. These can be evaluated after the focused workflow demonstrates useful defect detection and manageable maintenance cost.

Implementation status: D2 merged in PR #109 (`6dc67ed`), and E merged in PR #110
(`4808b10`). F extends the same tooling to reassembly and masking; its commands,
boundary distinctions and remaining review/evidence gaps are documented in the
[verification guide](../verification/README.md#reassembly-and-masking-evidence-phase-2-increment-f).
F is the final planned increment. Implementation and machine checks do not replace
the revision-specific human review required by the Phase 2 completion criterion.

### G: Close evidence reliability and remaining boundary tests

Baseline: D2 #109 (`6dc67ed`), E #110 (`4808b10`) and F #111 (`f3d5ed4`).
Investigate release run [42401715327](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42401715327),
preserving its failed receipt and exact NotifyReadFailure statement fingerprint.
A Timeout is inconclusive, never a Killed result. A passing retry is insufficient.

Acceptance criteria:

- Diagnose and bound the critical experiment, with repeated assertion-based
  detection, unchanged independent critical gates and measured CI budgets.
- Exercise parser completion fallback and cancellation, separate observer
  completion/error routes, active periodic-ping cleanup and custom calling
  SynchronizationContext behavior using causal barriers and injected delegates.
  Each gap needs a passing baseline and an isolated defect rejected at its intended
  assertion. Keep the real loopback tests and bound diagnostic waits/cleanup.
- Prefer internal seams; preserve runtime intent and public API. Any runtime
  correction needs its own scope, regression and evidence.
- Update requirements, boundaries, review notes and fingerprinted dispositions.
  Keep Killed, Survived, NoCoverage, Timeout, CompileError and Ignored distinct.
  Map the new tests/independent experiments to CLOSE-004/CLOSE-005.
- Capture unchanged source/config hashes, versions, commands, exit codes,
  timestamps, CI identity, library target and actual host runtime. Reject mixed
  source identities and incomplete experiment evidence. Human review remains
  unsatisfied until an actual revision-specific decision is recorded.
- Pass Release build, five library-target tests, 70% coverage, pinned proof,
  trust/generated-code checks, tooling/failure fixtures, all 15 Dafny mutations,
  ordering/new defect experiments and a complete Stryker run. Verify the evidence
  gate again on the release-branch run after merge; repair failures by follow-up PR.

Native-copy and compiler/runtime trust, exhaustive asynchronous verification and
additional runtime hosts remain outside G. Remaining gaps must be recorded, not
silently converted into equivalence. See [G evidence](../verification/close-evidence-g.md).
