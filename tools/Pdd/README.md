# Repository PDD tooling

`Pdd` is a standalone .NET 11 console tool for this repository. Build it without
building the WebSocket solution:

```bash
dotnet build tools/Pdd/Pdd.csproj -c Release
dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release
./scripts/pdd.sh validate
```

`validate` also checks RFC citations, rule 1 of the [rules for verification
work](../../verification/README.md#rules-for-verification-work):

- Every method, function, predicate, lemma, constructor and iterator in each
  `verification/*.dfy` file, the `{:extern}` declaration included, needs a
  citation in the block of `//` lines directly above it. `@` attribute lines may
  sit between; a blank line or a `/* */` comment may not. For the first member
  of a class with no comment of its own, the block directly above the class
  line, past its `@` lines, is read instead. Comments, strings (verbatim ones
  too) and character literals are blanked before declarations are looked for;
  block comments nest, and a prime after a name (`x'`) is part of the name.
- A citation names RFC 6455 and a section number in the same block ("RFC 6455
  section 5.3", or "RFC 6455 sections 5.2 and 5.5" followed by "Section 5.2:"),
  or says `Library policy:` and then what the policy is. Both may be anywhere in
  the block.
- A line with a declaration keyword that does not start a declaration the check
  reads, such as a member on the same line as its class, fails as an
  unrecognised declaration form, and so does a second declaration on one line,
  so that no declaration is skipped. The `by method` of a function by method
  belongs to the function. Only the text after `//` counts as the citation.
- Every requirement's `origin` in `verification/requirements.json` needs an RFC
  6455 section link (`https://www.rfc-editor.org/rfc/rfc6455.html#section-…`) or
  an entry that starts with `Library policy:` and states the policy.

The check sees that a citation is there, not that it is the right one; that stays
a review question.

There is one console project and one test project. Neither is in the production
solution or referenced by a published package. The console uses only standard
.NET facilities; the test project uses the repository's existing xUnit and
Microsoft.NET.Test.Sdk versions.

Keep this a small repository tool, not a general-purpose PDD framework. The
previous implementation was approximately 840 Python lines plus 610 test lines;
that is a scope reference, not a strict C# line limit. Every abstraction must
serve a current workflow. Use direct commands, shared types and the small process
runner. Do not add plugin systems, generic workflow engines, dependency-injection
frameworks, speculative extensibility, architectural layers or more projects
without a demonstrated current need. The schema evaluator supports only the
checked-in schemas' keywords; the source locator is deliberately not a language
parser. Keep validation, cancellation handling and evidence checks even when they
cost lines. Favor clear code over ceremony or compressed line counts.

## Commands

The shell entrypoint builds only the console and forwards its exit code and
signals. Its absolute path works from another directory; relative output paths
resolve from the calling directory. Source references always resolve from the
repository root. After building, `dotnet tools/Pdd/bin/Release/net11.0/Pdd.dll`
can be used directly with the same arguments.

| Command | Purpose |
| --- | --- |
| `validate [manifest.json]` | Validate requirements, boundaries, references and catalog-independent obligations, and the RFC citations above. |
| `contract-report --target REF --head HEAD --output FILE` | Compare committed trees from the actual target's unique merge base; requires semantic PR review. |
| `mutations [--scope all|close|payload] [--output DIR]` | Verify baseline and isolated catalog edits (default: all 17). |
| `capture --job protocol --output DIR` | Capture manifest, tooling tests, proof/audit/generated-source check and Dafny mutations. |
| `capture --job tests --output DIR` | Capture Release restore/build, six library targets and the 70% coverage gate. |
| `test-target --tfm TFM --output DIR` | Run one library target and capture TRX plus actual host-runtime evidence. |
| `aggregate [--output artifacts/pdd]` | Validate and combine `protocol/`, `tests/`, `controlled/` and `stryker/` into `receipt.json` and `summary.md`. |
| `normalize-generated --file FILE --tool-directory DIR` | Apply the existing generated-C# normalization byte-for-byte. Called by `dafny-masking.sh`. |

`mutations`, `capture` and `test-target` refuse existing evidence directories.
Aggregation can be rerun; it never infers missing results. Exit 0 means the
requested check completed successfully, 1 means invalid data/failed execution or
an unsuccessful evidence gate, and 2 means invalid CLI usage. A successful
contract report is a review aid, not approval. A passing receipt machine gate
does not satisfy pending human review.

## Execution, budgets and cancellation

The Linux verification workflow still requires Bash, `setsid` (util-linux), curl
and unzip, plus the unchanged checksum-pinned Dafny 4.11.0 / Z3 4.12.1 bootstrap.
No Python interpreter is required. The process runner uses argument arrays,
streamed stdout/stderr and combined capture logs, wall-clock budgets and a shared
cancellation token. Linux child sessions are killed as a group on timeout or
SIGINT/SIGTERM, including descendants whose parent already exited. Temporary
mutation source trees are disposed after success, failure or cancellation. A
SIGKILL or lost runner cannot guarantee cleanup or artifact upload.

Mutation wall budgets remain 180 seconds for the baseline and 90 per mutant;
`--baseline-budget` and `--mutant-budget` override them. The per-obligation Dafny
limit remains 60 seconds, configurable with `--verification-time-limit`. The
existing generation script retains its own 30-second obligation limit. `HeaderRoundTrip`
alone carries `{:timeLimit 120}`, which overrides both limits for that method. No proofs
are skipped and no automatic retry changes classification. Capture and
per-target commands have a new 1,800-second default wall budget per command,
overridable with `--command-budget`. Git and version probes are bounded too.

`job.json` and target `run.json` are saved before commands run. Writes replace
JSON atomically. Completed executions also retain `execution.json` with timeout,
interruption and launch details. Unfinished commands remain `not-run`; completed
unsuccessful execution is `failed`; later commands are `skipped`. Dafny
`inconclusive`, `detected` and `survived` outcomes remain distinct. Process logs
for capture live under `commands/NAME/` so they cannot collide with raw mutation,
coverage or test output directories.

## Compatibility and resolved findings

Schema versions, requirement IDs, receipt/job field names, the library targets (six since 10.4.0),
raw artifact locations and targeted Dafny diagnostic rules are retained. The
Python baseline tests were run before removal. Reference JSON, TRX and log
fixtures and Python-produced expected semantic outputs are retained under
[`verification/tests/fixtures`](../../verification/tests/fixtures). C# tests
compare gate results, each obligation status, accepted/rejected jobs, host
runtimes, test outcomes, coverage and mutation classifications against these
outputs. Fixture command arrays describe historical Python runs; they are data,
not an interpreter dependency. Timestamps, temporary paths, interpreter-specific
error wording and tool-version metadata are not equality assertions.

Intentional differences are explicit:

- Source identities now include `tools/`, so changed C# tooling cannot share an
  identity with unchanged tooling. NUL-delimited Git paths support unusual names.
- JSON duplicate keys are rejected consistently, including receipt and report
  input. Duplicate mutation experiment IDs are rejected instead of taking the
  last record. Evidence log paths cannot escape their job directory. Verifier
  version records must identify the pinned versions; failed probes and explicit
  timeout/interruption/launch-error flags invalidate claimed mutation success.
- A proof record claiming `passed` with a nonzero exit or missing execution/log
  evidence is changed to `failed` on the obligation too. Previously the overall
  gate failed but the proof obligation could still claim success.
- Verifier summaries containing timeout/resource-limit/inconclusive diagnostics
  cannot pass even if they also contain a positive verified count and zero errors.
- Capture and target execution now have explicit budgets and SIGTERM cleanup;
  execution detail files and `tools.pdd` replace the obsolete Python version.
- Contract summaries use Git's unified diff formatting instead of Python's
  `difflib`; headers/hunks may differ. Target/head/base identities, review warnings
  and the full committed source diff remain present. This does not add a semantic
  weakening detector or establish approval.

E uses these same capture/aggregate commands. Controlled schedules,
revision-bound human review and separate Stryker evidence remain required;
Stryker results must never become Dafny mutation detections. F extends the
repository's manifests/catalog and explicit checks for reassembly and masking,
without introducing a generic workflow system.

## D2 validation and size

The migration's local CI-equivalent pipeline passed the pinned proof/audit and
byte-for-byte generated-source check, baseline plus all five targeted mutations,
185 tests for each of five library targets, and 82.3% line / 75.6% branch coverage
(the line gate is 70%). The full live receipt matched Python aggregation of the
same evidence after excluding `created_at`; all six live Dafny classifications
also matched. All 38 original Python tests passed before their removal.

The C# suite has 119 cases, including the original failure modes, fixture semantic
comparisons and real timeout/SIGTERM cleanup of descendants, orphaned children
and nested sessions. The implementation is 1,128 C# lines and tests are 657 C#
lines (`wc -l tools/Pdd/*.cs tools/Pdd.Tests/*.cs`, including whitespace/comments,
excluding generated `obj/` code and JSON/TRX/log data). That is about 34% more
implementation text than the 842-line Python baseline; the 141-line shared model
and JSON helper file, explicit CLI and process/cancellation handling account for
most of the increase. There are exactly two projects and no new console package
dependencies. Test references are the existing Microsoft.NET.Test.Sdk 18.11.0,
xUnit 2.9.3 and xunit.runner.visualstudio 4.0.1 versions; no new package family or
architectural framework was introduced.

Increment E adds `controlled-defect --output <fresh-directory>` and capture jobs
`controlled` and `stryker`. Aggregate now requires all four jobs (`protocol`,
`tests`, `controlled`, `stryker`) with matching source and CI identities. See
[controlled schedules](../../verification/controlled-close.md) and the
[Stryker pilot](../../verification/stryker-close-pilot.md). Stryker dispositions
are source-bound; an untriaged outcome fails the gate, independent of its score.

F extends the existing manifests, catalog runner and receipt checks to reassembly
and masking, with no new projects, packages or production algorithms. Method-scoped
edits distinguish `Merge` from `MergeScalar`; strict pinned diagnostics now also
recognize mapped loop invariants and a frame violation. Multiline contracts and
`modifies` clauses resolve explicitly. Recorded diagnostic fixtures exercise wrong
clauses/files, unrelated errors, launch failures, interruption and timeouts. The
historical Python compatibility projection stays Close-only; separate tests require
the new obligations to fail when only those historical receipts are supplied.

G adds `boundary-defects --output <fresh-directory>` to the controlled capture.
It runs nine isolated source edits (five process repetitions for read-failure,
13 experiments total) and validates named assertion failures and actual runtime.
The aggregate rejects missing repetitions, wrong edits, mismatched source hashes,
compile/process failures and unrelated assertions. The original ordering gate
remains independently required. See `verification/close-evidence-g.md`.

P3-A adds `queued-defect --output <fresh-directory>` to controlled capture and
`queued-probe --output <fresh-directory>` for the manual 50-repeat smoke probe.
The isolated capacity-two frame gate must trigger the named overlap assertion;
source/CI identities, exact edit, runtime and execution/TRX are checked. No G gate
or budget changes. See [queued ownership](../../verification/queued-write-ownership.md).

P3-B adds `lease-defects --output <fresh-directory>` to controlled capture and
`lease-probe --output <fresh-directory>` for 50 manual repetitions. The three
isolated early-return, missing-return and wrong-count edits must fail their named
assertions; aggregate checks exact edits/source, all cases, execution and runtime.
See [sender leases](../../verification/payload-lease-ownership.md).

P3-C adds `retention-defects --output <fresh-directory>` to controlled capture.
Two isolated control-total and late-scratch edits must fail named assertions;
aggregation checks Release configuration, exact source/edit, cases, execution and
runtime. The existing REASSEMBLY-001-TEST adapter obligation includes the new
six-target tests without expanding its proof. See [parser retention](../../verification/parser-retention.md).

P3-D adds `reconnect-defect --output <fresh-directory>` to controlled capture.
The missing-removal edit must fail the old service identity assertion after all
nine reconnect cases pass. EPOCH-001-TEST requires six-target tests and this
source-bound experiment. Concurrent active subscription policy remains deferred.

P3-G bounds post-kill process/output draining at two seconds. If an escaped orphan
retains a pipe, cancellable reads stop and `cleanup_incomplete` is recorded with
`cleanup_seconds`; the timed-out/interrupted execution remains failed. Linux
session/tree cleanup cannot discover a reparented descendant that created its own
session. Other OSes retain best-effort process-tree cleanup, without a Linux group
claim. These bounds assume the OS services process operations and local log I/O;
a stopped machine or killed runner cannot guarantee return or upload.

The manual exploration runner accepts `--output <fresh-directory>` and refuses
overwrite. It saves command attempts before launch and retains timeout, launch and
interrupt failures and before/after source identity, including unsuccessful runs.

Ordering and G boundary assertion validation also requires raw execution records.
Exit 1 plus a matching assertion cannot hide timeout, interruption, launch failure
or incomplete cleanup; missing execution records fail closed.
