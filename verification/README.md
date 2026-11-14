# Verified WebSocket protocol core

The library calls C# generated from Dafny for synchronous protocol decisions and byte operations. `Masking.dfy` contains the original whole-array masking pilot and includes `Protocol.dfy`, which implements the remaining core. `generated/Masking.g.cs` contains both modules. Generated types are internal.

This work implements the five stages in the [adoption plan](../docs/dafny-pdd-plan.md): header validation, fragmentation, outgoing framing/masking, handshake/Close predicates, and integration checks. It does not establish full RFC conformance or prove the asynchronous client.

The proposed [PDD phase 2 plan for 10.2.0.0](../docs/pdd-phase-2-plan.md) adds requirement traceability, repeatable mutation checks, boundary review, CI receipts and controlled asynchronous tests, starting with Close handling.

## Rules for verification work

These rules hold for every change to the Dafny contracts, the [requirement map](requirements.json),
the [boundary register](boundaries.json), the mutation catalogs and the tests that check the protocol
core, whoever makes it.

1. **The RFC wins.** When a requirement is in question, RFC 6455 decides: not an agent's reading of
   it, and not the owner's. Each proved contract names the RFC section it comes from, or says
   "Library policy:" and what the policy is. Library policy covers choices the RFC leaves to the
   implementation, such as size limits, and never overrides a MUST. Where the code, a contract or a
   test disagrees with the RFC, the disagreement is written down and resolved in the RFC's favour.
   The RFC's errata are reviewed before a contract is frozen ([adoption plan](../docs/dafny-pdd-plan.md));
   a reported erratum is named beside the contract it concerns, as for the Close reason in `CloseValid`.
   The one deliberate departure is opt-in and outside the proved core: `ExcludeZeroApplicationDataInPong`
   sends the one-byte empty Pong that Slack's RTM API expects (see the README), with no RFC framing
   guarantee.
2. **Expected bytes never come from production code.** A test takes its expected bytes from the RFC,
   such as the masked "Hello" of section 5.7 in `Decode_MatchesRfc6455HelloVector`, writes them out
   by hand, or computes them in the test from the RFC's rule, as `Decode_PreservesScratchKeyAndRoundTrips`
   does with `b ^ key[i % 4]` from section 5.3. It never computes them with production helpers, which
   would let the code check itself. A verified reference such as the generated `MergeScalar` may be
   compared beside independent bytes, never instead of them
   (`BulkMergeMatchesScalarProofAndIndependentConcatenation`). This was a plan rule for phase 3
   ([`docs/pdd-phase-3-plan.md`](../docs/pdd-phase-3-plan.md), lines 612 and 613: "Keep independent
   expected bytes; do not let production helpers calculate their own oracle") and is now a standing
   rule.
3. **See a new test fail on the old code.** Before the fix that makes a new test pass is trusted, the
   test is run against the code before the fix and seen to fail, for the reason the test names. A test
   that passes on the old code, or fails there for another reason, does not show the fix. The failing
   run is recorded: the commit it ran at, the command, the failure line, and any change the test needed
   to run on the old code. The
   [header length replay](../docs/header-length-validation.md) shows why: the test for non-minimal
   lengths first passed on the 9.2.0 parser for the wrong reason. That parser accepted the header and
   then failed on the missing payload, with the exception type the test expected.

## Requirement maps (phase 2, increments A and F)

The [manifest](requirements.json), [v2 JSON Schema](requirements.schema.json) and
[intent review](close-intent-review.md) separate sequential Close decisions from
asynchronous caller responsibilities. Validate them with the .NET 11 SDK (standard library only):

```bash
./scripts/pdd.sh validate
dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release
```

The validator also accepts a manifest filename; source paths always resolve from
the repository root, independent of the working directory. It rejects unknown
fields (including run results), duplicate IDs/JSON keys, missing obligations,
dangling assumptions, missing files, paths outside the repository and unresolved
symbols, clauses or test identities. The malformed-manifest fixtures are in
[tests/invalid-requirements.json](tests/invalid-requirements.json).

Reference resolution is deliberately narrow and fails on ambiguous declarations:

- Dafny identities use `module.class.method`, not the generated C# namespace.
  The resolver finds the named module/class and a unique static method declaration
  in that class, then matches each complete `requires`/`ensures`/`modifies` clause in that
  method's header after folding indented continuation lines and normalizing whitespace. The known `{:isolate_assertions}` method attribute is supported. Clauses in other methods do not
  count. No source line number is an identity.
- C# identities use `namespace.class.method` in the named file. The resolver
  supports expression-bodied methods, file-scoped namespaces and uniquely named methods in the declared
  class. A test must additionally have an adjacent `[Fact]` or `[Theory]` attribute
  (with optional data attributes). Theory rows share the method identity.
- Comments and ordinary/verbatim strings are masked before declaration searches.
  This is a source locator, not a Dafny/C# parser or compiler: overloads, partial
  classes across files, raw strings, block namespaces and more complex declaration
  syntax require an explicit resolver extension. It does not resolve call graphs,
  preprocess conditional compilation or establish semantic coverage.

The [standalone .NET tooling](../tools/Pdd/README.md) replaces the original
Python implementations in phase 2 increment D2. It builds independently of the
production solution, with one console project and one test project.

The small schema evaluator supports only the keywords used by the checked-in
schema and rejects unsupported keywords. The schema is also usable by JSON Schema
2020-12 tooling; cross-reference checks require this validator. Changing the
manifest shape requires a deliberate schema/version and validator update.

All listed obligations are required, including deferred ones. `delivery` records
where an obligation is implemented (`existing`, B, C or E), **not a pass status**.
Existing proof/test obligations must resolve to declarations; future tests and
mutations have descriptions and stable obligation IDs without invented file or
test references. Review obligations point to the matching requirement section in
the intent review. CLOSE-004/005 are tested-only and have empty contract lists.
The v2 manifest references the separate boundary register described below.
Validator success does not mean a requirement is satisfied: mutation runs,
E controlled schedules and PR review remain separate obligations. Receipts and their aggregation belong to D.

## Close mutation checks (phase 2, increment B)

```bash
./scripts/pdd.sh mutations --scope close
dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release
```

The [Close catalog and runner guide](mutations/README.md) describes five exact
body edits covering duplicate/suppressed send, state-bit loss and
premature/suppressed release. The runner verifies both unchanged Dafny modules
first, then each mutant in a separate temporary tree with the same pinned
Dafny/Z3 toolchain as generation. Only rejection at the mapped postcondition
counts as detection. Surviving mutants and inconclusive experiments fail the PR
gate. Generated C# and production sources are never edited by the runner.

Each run writes fresh logs, source snapshots/hashes, versions, commands, budgets,
exit codes and matched clauses under `artifacts/close-mutations/`. CI uploads
these alongside the existing verification evidence, including on failure.
This is mutation evidence, not the aggregated requirement receipt planned for
increment D, and it does not establish specification completeness or the
asynchronous guarantees of CLOSE-004/CLOSE-005.

## Boundary register and contract changes (phase 2, increment C)

[boundaries.json](boundaries.json) replaces A's inline seeds, preserving their IDs
and assumption wording. The [v1 boundary schema](boundaries.schema.json) requires
an assumption, implementation owners by file/qualified method, dependent requirement
IDs, test obligation IDs, exclusions and an intent-review anchor. The requirements
schema is now v2 and names the register through `boundaries_file`; v1 manifests
must be migrated rather than silently accepted. The existing validation command
checks both files. Owners resolve using the same C# mechanism described above;
dependencies must exactly match the requirements' assumption references, and test
IDs must match all test obligations of those dependent requirements. Test details
and delivery stages remain in the requirement manifest, so E's controlled schedules
are visible obligations, not invented passing tests.

The initial register covers state-lock ownership, reservation versus completed
writes, failure/abort paths, timer delivery, and observer/disposal progress.
F adds buffer ownership, native-copy implementation, retained-fragment totals and masking adapter obligations. UTF-8 and handshake parsing remain outside this register expansion. The generated
[trusted-boundaries.md](trusted-boundaries.md) is still exclusively the
machine-compared Dafny external-contract audit; the existing `--check` gate is
unchanged. A handwritten boundary entry cannot authorize a new external contract.

For a committed topic branch, fetch its actual PR target and run:

```bash
git fetch origin releases/10.2.0.0
./scripts/pdd.sh contract-report --target origin/releases/10.2.0.0 --head HEAD --output artifacts/pdd/contract-changes.md
```

`--target` is mandatory; substitute the actual target if the PR is retargeted.
The report records target, head and merge-base SHAs. It summarizes requirement
intent and boundary/audit changes, includes complete diffs for every changed
Dafny file (including `requires`, `ensures`, supporting definitions and external
contracts), and includes the full repository source diff so changes outside these
summaries remain reviewable. Added and deleted files are included. Missing refs,
missing history, ambiguous merge bases and malformed intent data fail the command.

CI's PR-only `contract-change-report` job checks out the PR source head, fetches
the event's actual base SHA with full history, and uploads the Markdown report.
It compares the source head rather than GitHub's synthetic test merge commit.
Other verification/build jobs retain their existing checkout and gates. The job
summary points to the artifact; a successful job means the report was produced,
not that guarantees were accepted. Rerun on the current PR revision/target before
accepting changes if either has advanced.

This is conservative textual review: any committed difference requires semantic
review, including formatting changes. It does not parse Dafny, classify logical
strength, resolve helper dependencies, establish equivalence, or detect every
weakening. Full Dafny diffs deliberately preserve multiline clauses and helper
bodies instead of relying on a keyword-only extraction. Only committed trees are
compared; local edits and untracked files are excluded. Binary changes are Git
binary patches, not a semantic summary. This tool runs no proof or test and
provides no approval or receipt. Reviewers must record the accepted decision and
reviewed head revision through normal PR review; `approved: true` in a file is
not acceptance evidence. The boundary schema rejects such fields.

Tooling tests use temporary Git histories to exercise target divergence,
precondition/postcondition and helper changes, trust expansion, caller changes,
file addition/deletion, missing/unrelated refs and malformed registers. Run them
with the same tooling test command above. Increment D will add run receipts; this
report does not aggregate evidence or close E's asynchronous testing gaps.

## Reading the proof boundary

Dafny verifies the implementations and mathematical lemmas in our `.dfy` source. The compiler translates executable implementations into C#; it omits proof-only lemmas. Dafny does not separately verify the generated C# text, and it does not verify any of our handwritten C# implementations or test harnesses. We trust the verifier, solver, compiler and runtimes, and test the compiled integration.

A proof shows that the code meets its contract, not that the contract says what the RFC says. [Weakening the header length contract](contract-weakening.md) weakens `HeaderValid` in two places and records what the verifier, the generated-code check, the tests and the mutation suite each do.

A lemma supports a proof of the Dafny implementation. It is not a proof that handwritten C# is logically equivalent to that implementation. The C# callers remain responsible for establishing preconditions, preserving buffer ownership and applying the generated decisions correctly.

`NativeCopy.CopySlice` is the one explicit external implementation contract. Its handwritten C# adapter calls `.NET Buffer.BlockCopy`; Dafny assumes the contract rather than proving that implementation. Production `Merge` has a concatenation proof conditional on that assumption. `MergeScalar` proves the scalar algorithm without the external copy contract.

The merge comparison tests use the generated `MergeScalar` as a verified reference, alongside independent expected bytes. Passing comparisons establish agreement for the tested inputs only. They do not prove the C# test harness, prove the native copy implementation or transfer the reference algorithm's proof to other code. Tests that directly call generated protocol methods likewise check integration behavior; the tests themselves are not formally verified.

See the [proof-boundary diagram and assurance table](../docs/protocol-verification.md#what-is-proved-assumed-and-tested) and the [verified-reference testing diagram](../docs/protocol-verification.md#testing-against-a-verified-reference) for the distinction between **proved**, **assumed** and **tested** behavior.

## Contracts used by production

| Area | Dafny symbol and guarantee | C# caller and remaining boundary |
| --- | --- | --- |
| Header parsing | `DecodeHeader`: NeedMore/Reject/Accept and bounded canonical lengths. `Admit` composes decoding with `Step`, requiring a legal message transition and a bounded aggregate before accepting a header. `ReadLength` equals a big-endian byte fold. | `DataframeParsing.ReadDataframeAsync` owns stream reads, cancellation and payload allocation. It supplies the current message state/total, reads only the requested extended bytes, and allocates/reads a payload only after admission. Stream reads and ordering of these C# calls are not proved. |
| Fragmentation | `Step`: exact legal state/opcode transitions; rejection preserves state; accepted accumulated sizes remain bounded; controls preserve pending state except Close; completion requires FIN. | `WebsocketParserHandler` applies each transition and retains segments. Generated `Merge` proves that the new buffer equals the ordered concatenation of all supplied arrays and leaves inputs unchanged, assuming the explicit `NativeCopy.CopySlice` contract for .NET bulk copy. `MergeScalar` independently proves the per-byte algorithm without that assumption. The C# invariant connecting retained arrays to the transition total, metadata updates and observer emission remains reviewed/tested. |
| Outgoing frame sizing | `Outgoing`: canonical header size, bounded total size, defined FIN/opcode mapping and control-frame constraints. | `WebsocketSenderHandler` validates enum ranges before conversion, rents the buffer and uses the returned sizes. |
| Header encoding | `WriteHeader`: exact marker and network-order length bytes; key/payload region unchanged. | Sender supplies the validated sizes. `HeaderRoundTrip` calls the actual encoder and decoder and proves equal lengths and canonical header sizes for every accepted binary-frame payload size. It clears MASK before using the server decoder; this is a length-header theorem, not a full masked-payload or whole-protocol equivalence theorem. |
| Payload masking | `CopyMasked`, `MaskSlice`: exact per-byte XOR, preserved source and bytes outside the destination slice, bounded indices and termination. | Sender supplies source/destination bounds. Copy-and-mask snapshots the key bytes; in-place masking proves the key region stays unchanged. Copying rejects source/destination aliasing. String encoding and RNG remain .NET dependencies. |
| Opening handshake | `HandshakeValid`: acceptance requires every supplied condition. | Request-specific key retention, SHA-1/Base64, HTTP version/header/token parsing, duplicate handling and subprotocol membership are C#/.NET boundaries, tested with parser and loopback cases. The boolean predicate does not prove those computations. |
| Closing payload | `CloseValid`, `CloseCode`: length, status-code ranges and supplied UTF-8-validity condition. | `ProtocolPayload` reads network-order status and uses a strict .NET UTF-8 decoder for reason text. Complete received text messages are validated after reassembly, so characters may span fragments. |

The synchronous contracts assume no concurrent mutation of supplied arrays. ArrayPool ownership, async writes, scheduling, cancellation, observer delivery, and teardown remain outside the proofs. Existing concurrency and teardown tests remain in place. `SendStep` proves legal outgoing opcode/state transitions, preserved pending state for controls, and prohibition of data sends after failure or Close. The C# data-message semaphore spans an entire list and the TCP write semaphore covers transition plus wire write; these ownership/scheduling guarantees are tested, not proved by Dafny. Manual continuation calls still require one application owner. The legacy single-byte empty-Pong compatibility option bypasses standard framing and has no RFC framing guarantee.

Receiving masked server frames is now rejected. Consequently the original `Masking.Apply`/`WebsocketMasking.Decode` pilot remains tested but is no longer used by the receive path; outgoing masking uses the verified slice kernels. Their contracts allow pooled buffers without allocating a temporary key or payload array.

## Protocol corrections

The implementation rejects malformed headers, nonminimal lengths, unsupported opcodes/RSV bits, orphan continuations and new data frames during an unfinished message. Aggregate size and fragmentation checks run before allocating or reading that frame's payload. Reassembly fixes Length and Fragment metadata, and validates UTF-8 only when a complete text message exists.

Handshake success is reported after validation against the actual request key. Invalid responses fail at the header terminator, including when a server keeps the connection open. The delegate captures headers directly because HttpMachine's completed response snapshot may not yet exist at that point. A missing selected subprotocol is allowed; an unoffered or multiple selection is rejected. Extensions are unsupported and rejected. Protocol-owned request headers cannot be overridden through custom headers.

Close validation rejects one-byte payloads, forbidden/unassigned status-code ranges, and invalid UTF-8 reasons. `CloseStep` additionally proves monotonic sent/received/terminal flags, at most one send decision, and a release decision only after both directions or timeout/failure. The sender consumes these decisions under its state lock; only the release decision completes its close waiter. Pong is permitted after a locally sent Close until the peer responds, and queued Pongs are suppressed after peer Close or terminal failure. Asynchronous disposal awaits the peer response or a bounded timeout after the Close write. Timer progress, lock ownership, write completion, close-code selection and the complete async handshake remain outside the proofs. Strict string encoding rejects invalid UTF-16 input; there is no new proof of incremental UTF-8 validity for manually supplied outgoing byte fragments.

Contracts follow [RFC 6455](https://www.rfc-editor.org/rfc/rfc6455.html), especially sections 4.1, 5.2–5.5, 7.4 and 8.1. Generated integration follows the [Dafny C# guide](https://dafny.org/dafny/DafnyRef/integration-cs/IntegrationCS).

## 10.1.0 state and composition proofs

- `Admit` joins header decoding and fragmentation admission. Rejection preserves the supplied state/total; an accepted data frame fits the remaining message budget.
- `SendStep` checks the actual wire opcode/FIN, including the continuation opcode produced by `Outgoing`. `SendFailed` is irreversible for data. A list is prevalidated and keeps the data-message gate across all fragments; controls use the independent frame gate. Failures after a completed fragment poison the message state; a possible partial write terminates all framing.
- `Merge` copies retained arrays into an exact-sized buffer with a quantified concatenation contract. `PartsPrefix` proves the prefix/length relationship used by the loops. Its single external leaf, `NativeCopy.CopySlice`, is implemented by `Buffer.BlockCopy` in `Helper/VerifiedNativeCopy.cs`: the proof assumes .NET copies the specified source bytes and preserves everything else. `MergeScalar` proves the complete per-byte reference algorithm without that external contract. Tests compare both with independent concatenation and check the native adapter's preservation behavior. The caller must establish that `total` equals the sum of segment lengths and that arrays cannot change during copying.

  This is an explicit performance/trust tradeoff. A local isolated benchmark found the verified scalar copy about 7–14 times slower than `Buffer.BlockCopy` for two-segment 1 KiB/64 KiB messages. Production retains bulk copying; the benchmark reports both implementations. No claim is made that .NET's bulk-copy implementation is verified by Dafny.
- `CloseStep` uses sent/received/terminal bits. The sent bit reserves the single Close attempt before the write; C# must await that write before releasing the transport. Receive and failure notifications wake the waiter. Synchronous disposal may abort at its separate bound, and is not covered by the graceful-completion guarantee.
- `HeaderRoundTrip` composes `Outgoing`, `WriteHeader`, `ReadLength` (through `DecodeHeader`) and the `NetworkWord` lemma. It covers lengths through `int.MaxValue - 14` without allocating payloads. Handshake parsing, cryptography and UTF-8 remain C#/.NET computations.

Regression tests exercise concurrent list/single sends, controls during lists, invalid later text items, cancellation after a first fragment, failed writes, manual sequencing, early header rejection without payload bytes, ordered merge, duplicate Close suppression and delayed peer Close. A passing test is evidence for these selected schedules, not a proof of every possible async interleaving.

## Reproduce verification

Run from the repository root in the Linux x64 dev container:

```bash
./scripts/dafny-masking.sh --check
# After changing either Dafny source:
./scripts/dafny-masking.sh --write
./scripts/dafny-masking.sh --check
```

The script retains its pilot name but verifies both modules, including included files. It downloads the checksum-pinned official Dafny 4.11.0 archive and uses its Z3 4.12.1 solver explicitly. The .NET 11 SDK, curl and unzip are required. Verification errors and assertion timeouts fail the command. The tool cache is outside the repository.

The generated file is checked in; ordinary builds and package consumers do not need Dafny or Z3. Never edit generated methods. Postprocessing only normalizes the machine-specific solver path in embedded metadata, marks generated partial classes internal, and adds generated-code/nullable headers. CI compares regenerated output byte-for-byte and uploads logs and artifacts. Dafny's compiler, solver, runtime and this limited postprocessing are trusted.

`DafnyRuntime` 4.11.0 is a new package dependency. Its netstandard2.0 DLL is approximately 172 KiB and declares System.Collections.Immutable and System.Runtime.Numerics dependencies. Generated support types require it even where hot loops use only native integer/byte operations. Runtime preconditions are not automatically enforced by compiled Dafny; adapters establish them explicitly or rely on documented .NET contracts such as array length and ArrayPool.Rent capacity.

There are no `assume` statements, `{:axiom}` attributes or verification-disabled implementations in the Dafny sources. There is one explicitly trusted external contract, `NativeCopy.CopySlice`, backed by the .NET `Buffer.BlockCopy` adapter. The generator audits both Dafny source roots and compares the findings with [trusted-boundaries.md](trusted-boundaries.md), so a newly introduced assumption or external contract fails the check until the report is explicitly reviewed and updated. It deliberately uses `--allow-external-contracts` for this boundary; this flag does not prove the adapter. The remaining Dafny algorithms have bodies checked by the verifier; this statement does not include handwritten C#. Pure acceptance functions are executable specifications: their verification is not an independent proof that the RFC was interpreted correctly. Review the specification and adapters along with the proofs.

## Checks

```bash
dotnet build src/main/WebsocketClientLite.sln -c Release
./test-all-tfms.sh Release
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/protocol-coverage
./scripts/check-coverage.sh artifacts/protocol-coverage 70
DOTNET_TieredCompilation=0 dotnet run --project verification/benchmark/MaskingBenchmark.csproj -c Release
```

Use an empty coverage directory for each run because the existing coverage checker selects its first report. Generated source stays in the coverage report; handwritten adapters are not excluded. The test host is .NET 11 for every library TFM, including netstandard builds; this does not test every supported runtime/JIT.

Tests cover canonical length boundaries through 65536, integer limits without huge allocations, truncated headers, invalid masks/RSV/opcodes, control constraints, fragmentation transitions, message caps, UTF-8 split across fragments, Close payloads, request-specific accept validation, malformed negotiation and failure delivery with an open server connection. Byte-level tests compare with independent expected values, including the RFC Hello masking vector.

## Performance methodology

The benchmark compares the previous scalar loops with generated code using preallocated arrays, alternating order and medians of seven trials. It reports steady-state allocation counts separately. It also exercises fused outgoing copy-and-mask. Timing is not a CI gate because shared runner noise would make it unreliable.

The original pilot budget was zero per-call allocations, no more than 15% slowdown for payloads at least 125 bytes, and at most 20 ns added overhead below that size. Its whole-array loop met that budget on .NET 10.0.14. The sender experiment uses the same 15% budget for the measured payload sizes. These are local kernel measurements, not end-to-end network throughput or portable JIT guarantees.

## 10.1.0 local validation

- Pinned verifier: **41 verification results, zero errors**; regenerated C# matches the checked-in file. Counts include specification well-formedness, including the trusted primitive's contract, not independently proved RFC rules.
- Trust audit: exactly the two expected findings (preconditions/postconditions) for `NativeCopy.CopySlice`; both source roots are audited and the report comparison passes.
- Release solution build: **zero warnings/errors**.
- Target matrix: **184 tests passed per library target**, 920 executions, using a .NET 10 host.
- Coverage: **82.3% lines, 75.6% branches**, including generated code and the native adapter; the 70% line gate passes.
- Negative proof checks in temporary copies: unconditional Close release fails `CloseStep`'s postcondition; copying each segment at offset zero fails `Merge`'s concatenation assertion. Production sources were not mutated for these checks.

The benchmark command above reports the reassembly tradeoff as well as the existing masking kernels. Local .NET 10.0.14 medians for two-segment messages, including the destination allocation on every path:

| Payload bytes | Previous bulk copy ns | Verified orchestration + trusted bulk copy ns | Fully verified scalar reference ns |
| --- | ---: | ---: | ---: |
| 1024 | 42.58 | 43.30 | 330.72 |
| 65536 | 1149.09 | 1147.02 | 15806.92 |

These measurements support retaining the bulk-copy primitive in production; they are not portable timing guarantees. They were recorded before publication. Version 10.1.0 was subsequently published to NuGet.org and GitHub Packages, and [release 10.1.0.0](https://github.com/1iveowl/WebsocketClientLite.PCL/releases/tag/10.1.0.0) was merged into `main` through [PR #103](https://github.com/1iveowl/WebsocketClientLite.PCL/pull/103). Release and [post-merge CI](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42379096343) passed. `GitVersion.yml` was updated to 10.1.0 for that release.

## Historical 10.0.0 local results

- Dafny: **23 verification results passed, zero errors**, both on regeneration and a subsequent clean generated-source check. These include specification well-formedness checks and are not a count of independently proved RFC requirements.
- Negative proof check: changing the first fused copy operation from XOR to OR fails its quantified output contract.
- Release solution build: **zero warnings/errors**.
- Target matrix: **161 tests passed per library target**, 805 executions across netstandard2.0, netstandard2.1, net8.0, net9.0 and net10.0.
- Coverage: **79.9% lines, 74.3% branches**; the existing 70% line gate passes with generated code included.
- Packages: both local packages built with temporary package version `0.0.0-protocol`; a separate consumer restored and compiled against them for all five targets without warnings/errors. No package was published and `GitVersion.yml` was not changed.

Final sender measurements on .NET 10.0.14 compare equivalent scalar loops with runtime offsets/counts, matching the sender's pooled-buffer layout. All measured paths allocate zero bytes in the measured regions.

| Kernel | Payload bytes | Previous scalar ns/call | Verified ns/call | Verified/previous |
| --- | ---: | ---: | ---: | ---: |
| Copy and mask | 125 | 51.54 | 45.15 | 0.876 |
| Copy and mask | 1024 | 409.87 | 365.58 | 0.892 |
| Copy and mask | 65536 | 25598.97 | 22983.36 | 0.898 |
| In-place slice | 125 | 54.65 | 51.58 | 0.944 |
| In-place slice | 1024 | 453.33 | 425.46 | 0.939 |
| In-place slice | 65536 | 28308.30 | 26539.79 | 0.938 |

The active sender kernels meet the local budget. Copy-and-mask uses four-byte batches plus a short tail; in-place masking reads the preserved key region directly. Neither uses a variable-shift runtime helper in its hot loop.

The retained, unused receive-side pilot helper measured approximately 45–50% slower than its original scalar comparison in this final build, despite meeting its earlier pilot measurements. It is no longer on the production receive path because masked server frames are rejected. The benchmark continues to report it; the original pilot result is not a portable performance guarantee. Do not reintroduce that helper into a hot path without remeasuring.

## CI verification receipts (phase 2, increment D)

CI captures the existing protocol and build/test jobs independently, then runs
`evidence-gate` even when a dependency fails. Download the `pdd-receipt` artifact
for `receipt.json`, `summary.md` and the linked raw logs, TRX, coverage and Dafny
mutation results. The contract-change report remains a separate review aid;
it is never treated as approval. GitHub CI runs when `GitVersion.yml` changes in a push or pull request
to `releases/**`, on every pull request into `main`, or when manually dispatched
for release validation. A pull request into a release branch without that change
runs only the workflow's `scope` job, which skips the rest. Ordinary
changes must pass the local commands below before merging. `main` requires the
`build-and-test` check; do not require these checks on release branches, where
ordinary pull requests skip them. Release CI and package publishing are separate workflows;
the evidence gate does not block the publish workflow.

To reproduce locally, use a **new** output directory (capture refuses reuse):

```bash
./scripts/pdd.sh capture --job protocol --output artifacts/pdd/protocol
./scripts/pdd.sh capture --job tests --output artifacts/pdd/tests
./scripts/pdd.sh capture --job controlled --output artifacts/pdd/controlled
./scripts/pdd.sh capture --job stryker --output artifacts/pdd/stryker
# Local Stryker defaults to eight workers. GitHub Actions explicitly selects two.
# Optional override: --stryker-concurrency <positive integer>
./scripts/pdd.sh aggregate --output artifacts/pdd
```

Run these against an unchanged checkout. The receipt records the tested commit,
PR source head (`PDD_PR_HEAD`, null for local/push runs), worktree dirtiness,
SHA-256 hashes of tracked and unignored source, manifest, tooling (including `tools/`) and workflow
files, command arrays, exit codes, UTC timestamps, tool/SDK versions and CI run
identity. Source snapshots are checked before and after each job and compared
with the aggregator's checkout. CI run/attempt identities must also match.
Mismatches are retained as rejected evidence, never combined with valid results.
These are traceability artifacts, not signed attestations or tamper-proof records.

Every target has a fresh result directory and a recorded test command. TRX test
identities are resolved through `TestDefinitions/TestMethod`, including every
case of a named parameterized test. The host runtime comes from
`RuntimeInformation.FrameworkDescription` emitted **inside the test process**.
All six library targets currently use a .NET 11 test project; this is not a
six-runtime test matrix. The ordinary `./test-all-tfms.sh Release` command still
works; CI capture sets `PDD_TEST_RESULTS` to collect per-target evidence.

The machine gate requires successful manifest/fixture checks, proof plus audit
and generated-code drift checks, every catalogued targeted Dafny mutation,
Release build, the complete library-target matrix, named test obligations and
the existing 70% coverage gate. A zero verifier exit without a verification
summary is insufficient. Missing, malformed, failed, skipped and interrupted
results cannot pass. Commands not reached after a failure are `skipped`; commands
with no completed output are `not-run`. Partial job records are saved before
commands start; always-run uploads and aggregation preserve available evidence
on failure/cancellation where GitHub permits remaining steps/jobs to run. A
cancelled runner cannot guarantee artifact delivery; missing evidence fails.

A passing **machine gate does not mean all Close requirements are satisfied**.
Review obligations are recorded separately as `not-run`, pending revision-specific
acceptance through normal PR review. Increment E's controlled tests and separate
Stryker evidence are consumed from their separate jobs; missing results remain `not-run` and cannot satisfy those obligations. CLOSE-004 and CLOSE-005 still require revision-specific human review. This
increment does not consume approval booleans or infer review acceptance from
contract diffs. Future review ingestion must bind actual review decisions to the
reviewed source revision.

Receipt fixtures cover successful runs, actual failing commands, skipped and
interrupted results, absent jobs/logs/targets/runtime data, failed or skipped named
tests, missing mutations, malformed records, revision/hash and CI-attempt
mismatches. Run them with `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release`.

### Controlled Close evidence

[Increment E schedules](controlled-close.md) add deterministic barriers alongside
the real loopback tests. Capture `controlled` and `stryker` into separate job
folders beside `protocol` and `tests`, then run `./scripts/pdd.sh aggregate`.
All four captures must use the same unchanged source identity. The aggregate
gate requires the ordering-defect experiment independently of Stryker outcomes;
review acceptance is never inferred from either experiment.

## Reassembly and masking evidence (phase 2, increment F)

The manifest separately maps production `Merge` (REASSEMBLY-001), scalar reference
`MergeScalar` (REASSEMBLY-002), and the tested-only native copy implementation
(REASSEMBLY-003). Production concatenation relies on `NativeCopy.CopySlice`'s
external contract. Scalar concatenation does not; it is a reference path, not
production integration evidence. The native copy implementation has an explicit
proof/mutation exception, with its guard/source preservation test retained.

MASK-001 maps the original whole-array `Apply` helper; MASK-002 and MASK-003 map
the sender's `CopyMasked` and `MaskSlice`. Independent expected-byte tests cover
XOR, zero-length/prefix/tail/bulk cases, key/source preservation and the entire
outside destination slice. Aliased reassembly segments are checked against literal
concatenation bytes, fresh output and unchanged inputs for both variants. Existing
loopback, adapter, RFC-vector and integration tests remain in place.

Run `./scripts/pdd.sh mutations` for all 15 experiments or select `--scope close`
(five) / `--scope payload` (ten) for investigation. Only the full suite satisfies
the protocol receipt. See the [catalog guide](mutations/README.md) for exact
postcondition, loop-invariant and frame diagnostics and their contract mappings.
The four capture jobs above and aggregation use identical source identities; old
Close-only receipts cannot satisfy the new obligations. JSON records preserve
actual library target and host runtime separately.

This completes the planned implementation sequence A–F, not human acceptance of
Phase 2. The [intent review](close-intent-review.md) includes F's requirement and
boundary decisions but does not record reviewer approval. Review remains separate
and `not-run` in machine receipts. Native-copy correctness, toolchain trust,
concurrent caller ownership and the documented [Close pilot gaps](stryker-close-pilot.md)
remain explicit limits. No exhaustive schedule exploration or full RFC proof is claimed.

## Increment G Close evidence

The [G record](close-evidence-g.md) retains the failed post-F receipt and maps
parser/cancellation, observer, periodic-ping and synchronization-context schedules
to CLOSE-004/CLOSE-005. The controlled job now requires both the existing ordering
experiment and `./scripts/pdd.sh boundary-defects --output <fresh-directory>`.
Five independent process repetitions challenge read-failure notification. Every
experiment must fail its named behavioral assertion; timeouts and infrastructure
errors remain inconclusive. Source-matched aggregate evidence and revision-specific
human review remain distinct.
