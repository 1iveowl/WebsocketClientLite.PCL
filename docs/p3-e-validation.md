# P3-E: actual runtime qualification

P3-E changes test hosting and evidence tooling only. A–D were merged into
`releases/10.2.0.0` before work started at
`d5edabbc148001f8fdc9cafe6f6ac8e09d8e57be`. D's clean four-job receipt at
`3d47d5c79e1be2b7e9e34f2b41908f10acfce1f6` and its repeated reconnect checks
supply the immediate stable baseline. The Phase 3 plan identifies G's passed
post-merge run 42408136157 at `bed679e`; the retained G baseline receipt was
checked again. Its earlier F timeout remains failed historical evidence.

The checked P3-E implementation revision is
`6e95ce1` (full revision in every receipt). The final documentation/archive
commit does not relabel these runs as evidence for a later commit. No production
library code, package target, GitVersion.yml or core.12592 is changed.

## Qualification and policy

`TestHostTfm` selects the existing test project's actual target, defaulting to
net10.0. `LibTfm` independently selects the library. LatestPatch roll-forward
prevents an absent 8/9 runtime silently selecting 10. The test process reports
runtime patch, test and library target attributes, OS/version, OS and process
architecture, assembly full name, loaded path, MVID and SHA-256. Each manual cell
retains the expected built library and compares its bytes with the loaded hash.
Restore, build and test are separately recorded; successful restore is not a pass.

`test-host` uses existing Execute/JobReceipt/source identity and TRX parsing.
`aggregate --host-cells` is the focused host path of the existing receipt command;
it cannot replace the full four-job aggregate. It rejects dirty/incomplete or
mixed source/run-attempt evidence, missing/duplicate cells, wrong actual host,
OS or library, unsuccessful execution and incomplete A–D case counts. The full
aggregate additionally requires all five primary Linux cells to report .NET 10,
Ubuntu 24.04 x64 and the intended loaded library target. Its proof, coverage,
controlled-defect and mutation obligations are retained.

The workflow has an explicit manual `host_qualification` input. With it unset,
all existing release evidence jobs remain selected. With it set, only the narrow
host experiment runs; two fresh runner jobs per OS each execute the specified
library targets. Source bytes use LF checkout on every runner. No proof or
Stryker work is repeated on Windows/macOS. The primary jobs now name
ubuntu-24.04 explicitly; pinned proof remains on ubuntu-22.04.

PR #117 supersedes the plan's routine-PR placement proposal: routine changes
require complete local evidence; automatic GitHub CI is reserved for release
version changes. Windows mandatory promotion and the macOS weekly schedule are
therefore **deferred**, even after two clean manual runs. The candidate placement
labels in HostCells do not automatically promote an OS based on a run. A future
policy change needs explicit workflow/aggregate wiring, not just a placement-label
edit. It must retain two clean independent qualification runs, exact source
and host identity, and cost review; missing mandatory cells must fail. A scheduled
result belongs to its own revision/run and cannot satisfy a later PR by reuse.

## Lifecycle and runner check — November 8, 2026

Primary references were checked again:

- [.NET lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core):
  .NET 10 support ends November 14, 2028; .NET 8 and 9 end November 10, 2026.
- [.NET 10 supported OS versions](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md):
  Ubuntu 24.04, Windows Server 2022 and macOS 15 remain listed for the selected architectures.
- [Ubuntu lifecycle](https://ubuntu.com/about/release-cycle): Ubuntu 24.04 LTS
  standard maintenance runs to May 2029.
- [Windows Server 2022 lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-server-2022):
  mainstream support ended in October 2026; extended support ends
  in October 2031. This does not disqualify the selected .NET-supported OS.
- [GitHub runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners):
  ubuntu-24.04 and windows-2022 are x64; macos-15 is Arm64. Actual runner access,
  image and architecture are established by the jobs below, not the label alone.

GitHub reports this repository as public. Standard hosted runners are free for
public repositories under that reference; runner-minutes below are consumed
resources, not invented billed prices. No paid/larger runner was selected. There
is no fixed macOS end-of-support date asserted here; recheck Apple's servicing
and Microsoft's supported-OS table before subsequent qualification.

## Negative cases and failed diagnostic attempts

The tooling suite extends existing receipt fixtures with missing cell, duplicate
cell, wrong runtime major/target, wrong OS/version/architecture, substituted
library target/hash/path/MVID, wrong source and source-after, another run/attempt,
failed restore and timed-out execution. A positive complete source-bound fixture
must pass. A platform-neutral process-log readback test runs on each hosted OS.

The first host workflow at `7da01c9`, [run 42469025903](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42469025903), is retained as failed:
Windows stopped before tests because Process.Run read logs while its writers were
still open. Windows sharing rules reject that readback. Closing the writers
before readback fixes the tooling defect without changing Linux process-group
cleanup. macOS passed its tests but the identity validator expected a Darwin
string; actual .NET 10 reports `macOS 15.8.2`. The validator now checks that exact
OS family/major format, with a regression fixture. Neither was a library defect.

The first private legacy-host setup symlinked the system SDK. VSTest resolved the
system .NET installation, which had only 10, and aborted both 8/9 hosts. Copying
the SDK into the private root made VSTest use the installed 8.0.33 and 9.0.22
runtimes. This is a setup failure, not evidence that the test dependencies or
library fail on legacy hosts. Restore/build were already successful. No dependency
pinning or duplicate test project was needed. The bounded investigation ends here;
ongoing 8/9 runs after November 10 need a named consumer and maintenance owner.

The initial local full capture at `7da01c9` was explicitly stopped after the
five-target tests passed, while coverage/Stryker were running, to correct the
host tooling. Those records remain interrupted and never count as acceptance.
An initial fixture also caught a JSON node-parent ownership error in assembling
host results; returning a clone fixed it before the first implementation commit.
No retry silently changes any failed or interrupted outcome.

The next [host run 42469291427](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42469291427)
at `686d554` passed all six jobs, including two fresh Windows and macOS jobs.
Cross-platform source inspection then found 154 Windows hashes differed from
Linux/macOS. Every difference matched CRLF conversion exactly; 22 files already
matched and no difference was unexplained. Disabling core.autocrlf alone did not
force LF under the repository's text=auto attributes on Windows. Setting core.eol
to lf as well fixes checkout without normalizing or weakening receipt hashes.
The second run remains successful for its actual source bytes; final qualification
repeats on the corrected revision. The second local capture passed tests,
coverage, proofs/mutations and all controlled experiments; its Stryker capture
was deliberately interrupted to avoid finishing validation on superseded workflow
source. Both interrupted Stryker captures are retained separately.

The two interrupted local Stryker commands consumed 155.774 and 254.974 seconds
before cancellation. Their partial logs/execution records remain classified as
interrupted, and neither supplies mutation outcomes for acceptance.

## Scope and deferrals

No claim covers .NET Framework, Mono, Unity, browser/WASM, mobile/AOT, trimming,
musl or other architectures. Transitional netstandard2.0 on real 8/9 is deferred;
the native net8.0/net9.0 cells suffice for this bounded trial. All five package
TFMs remain. No public runtime behavior or API policy changes.

Process-group cleanup remains Linux-specific. Successful Windows/macOS commands
are qualified; arbitrary descendant timeout cleanup on those OSes is not. A lost
runner still cannot guarantee artifact upload. Human semantic review remains
separate from machine receipts. P3-F/G work is not started.

## Reproducing a cell

Build the existing console with `dotnet build tools/Pdd/Pdd.csproj -c Release`.
On the intended host, from a clean checkout with its matching runtime installed:

```text
dotnet tools/Pdd/bin/Release/net10.0/Pdd.dll test-host --cell windows-10-net10.0 --output artifacts/hosts/windows-10-net10.0
dotnet tools/Pdd/bin/Release/net10.0/Pdd.dll test-host --cell windows-10-netstandard2.0 --output artifacts/hosts/windows-10-netstandard2.0
dotnet tools/Pdd/bin/Release/net10.0/Pdd.dll aggregate --host-cells windows-10-net10.0,windows-10-netstandard2.0 --output artifacts/hosts
```

Each test-host output directory must be fresh. Other supported IDs are
`linux-10-<LibTfm>` for the five existing targets, `macos-10-net10.0`,
`macos-10-netstandard2.0`, `linux-8-net8.0` and `linux-9-net9.0`.
The tool itself stays on .NET 10; it launches the test project with the cell's
TestHostTfm and checks identity from inside that test process. For a simple
unreceipted diagnostic, use `dotnet test` on the existing test project with
`-p:TestHostTfm=net8.0 -p:LibTfm=net8.0`. That diagnostic alone is not a qualified cell.

The manual workflow entry is `gh workflow run ci.yml --ref <reviewed-branch>
-f host_qualification=true`. It does not publish packages or run the full release
gate. Leave the input false for the existing full release validation workflow.

The repository maintainer owns the next review: before any mandatory/scheduled
promotion, when a runner image/architecture or runtime major changes, and before
retaining legacy-host checks after lifecycle expiry. No automatic promotion or
continuing 8/9 maintenance owner is implied by these receipts.

## Final host evidence

[Manual qualification run 42469811451](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42469811451), attempt 1, passed at `6e95ce1`. The six jobs are independent fresh hosted runners: two per OS, with sequential library cells inside each job. All 18 cells passed 269 tests and the receipt gate. An independent local re-aggregation of all six downloaded artifacts checked their original run/attempt against the current clean source. All 176 source hashes match byte-for-byte across Linux, Windows and macOS; every before/after identity matches.

| Actual OS / architecture | Runtime | Library targets | Clean jobs / cells | Job wall seconds |
| --- | --- | --- | --- | --- |
| Ubuntu 24.04 x64 | .NET 10.0.14 | netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 | 2 / 10 | 176, 189 |
| Windows Server 2022 (10.0.20348) x64 | .NET 10.0.14 | net10.0, netstandard2.0 | 2 / 4 | 113, 133 |
| macOS 15.8.2 Arm64 | .NET 10.0.14 | net10.0, netstandard2.0 | 2 / 4 | 103, 127 |

Actual loaded assembly SHA-256 (the same within each OS/target across its two fresh jobs):

| OS / library target | SHA-256 |
| --- | --- |
| linux-10-netstandard2.0 | `2cdfbf49320974212efaf420c3a8a0fb396e8c4bbfff062849a118e03790330a` |
| linux-10-netstandard2.1 | `936f81c3f97a1f38aceac3ef81cce143ecd4d7a2a74357e7954d61361e59de67` |
| linux-10-net8.0 | `6204685bb568d4a033b487b2d86a0709d87fbefbd3fe7dd04dc628011630b54e` |
| linux-10-net9.0 | `5637305e0e44f94e03db5de7e5cee08fe055599080f13e56f5bbdaadbb5b7cf1` |
| linux-10-net10.0 | `73d6f0dd07d76bab0a506bbfb28abe34ce8bc32e68aa48c810cc936da339a5bc` |
| macos-10-net10.0 | `b647dd5f2ea4ff1c2fc2eb5bb051f6872557faefada095c86bf8281ce9eb5580` |
| macos-10-netstandard2.0 | `0e81c6981cc4f897d719519dfd6f7c963c3d1072f470035084a0a1b44e2eb9b6` |
| windows-10-net10.0 | `c6df454edc88e95a81f71090001c2038f2ad8a0458c56dc6c1066bf5f8f6ea1a` |
| windows-10-netstandard2.0 | `cf3f6876c5be59de5074f232f361b5d958d2e98557cf5f52f840c79a56311e05` |

Full assembly paths, MVIDs, assembly versions, runner image versions/names, SDK information, per-stage execution time and source identities are in the retained receipts and `independent-host-audit/summary.json`. Assemblies built at different OS checkout paths need not have identical hashes; each loaded assembly matches its own expected build.

Final transitional cells at the same source revision:

| Actual host | Tests | Restore / build / test seconds | Loaded assembly SHA-256 |
| --- | ---: | --- | --- |
| Ubuntu 24.04 x64 / .NET 8.0.33 | 269 | 0.735 / 1.714 / 25.604 | `7dcb98de84a0b967acd67586faad94596cddeda60529375be2f1f0c6c88883c4` |
| Ubuntu 24.04 x64 / .NET 9.0.22 | 269 | 0.693 / 1.035 / 25.565 | `146ec7616cd715b4301d5d39fbfbfe46c1736b9305d820c8c1a5626f882b90f8` |

The private runtime installation adds no repository package or system-wide SDK change. These are successful real 8/9 executions, not results inferred from installed SDKs or LibTfm. Repeated legacy runs were diagnostics/source synchronization, not promotion to mandatory ongoing support.

## Qualification cost

The final six hosted jobs consumed 841 seconds (14.02 runner-minutes), with a 189-second maximum job duration. Each is below the provisional ten-minute job budget. The two Windows jobs used 246 seconds together; macOS used 230; Linux used 365. A single pair of Windows library cells took 113–133 seconds including setup and receipt fixtures. No routine PR critical-path or billing increase is introduced because promotion is deferred.

These are six jobs in one workflow run, not ten normal CI samples or a latency percentile. Fresh checkout/build output does not imply an empty runner-provided NuGet cache. The receipts separate first and subsequent restore/build/test commands; no pristine-cache benchmark or peak-memory measurement is claimed. Hosted CPU/RAM allocation comes from the runner reference (Linux/Windows 4 CPU, macOS 3 M1 CPU); actual image/architecture and runner names are retained. Local machine information is in the environment archive.

The first diagnostic hosted run consumed 624 seconds (10.40 runner-minutes), including failed jobs. Those costs are additional to the final qualification, not omitted from its history.

The second diagnostic hosted run consumed 782 seconds (13.03 runner-minutes). All six jobs passed for their actual source bytes; the later cross-platform audit motivated the LF correction. Across all three hosted trials, the total was 2,247 seconds (37.45 runner-minutes), not a recurring CI budget claim.

## Final local repository checks

Clean source: `6e95ce1a66f61a8c62dc04c38aa8a07803e0b9ea`. The full local four-job receipt and source-before/source-after checks are retained separately from the manual host gate.

| Local command | Seconds |
| --- | ---: |
| tests/restore | 0.60 |
| tests/build | 1.48 |
| tests/matrix | 134.61 |
| tests/coverage-run | 27.05 |
| tests/coverage-gate | 0.01 |
| protocol/manifest | 0.69 |
| protocol/fixtures | 9.40 |
| protocol/proof | 10.45 |
| protocol/mutations | 142.96 |
| controlled/defect | 4.07 |
| controlled/boundaries | 22.22 |
| controlled/queued | 4.03 |
| controlled/leases | 12.25 |
| controlled/retention | 5.49 |
| controlled/reconnect | 16.31 |
| stryker/stryker | 1025.71 |

The complete aggregate **passed**. All five library targets passed 269 tests on
.NET 10.0.14; line coverage was 83.08% against the unchanged 70% gate. All 237
tooling cases, pinned proof/trust/generated-source checks, 15 Dafny mutations and
all ordering/G/A/B/C/D controlled experiments passed. Source-before/source-after
identities matched for every job. All 14 human-review obligations remain separately
not-run; machine evidence does not manufacture human acceptance.

Stryker 5.0.0 selected-scope results: 42 Killed, 27 Survived, 7 NoCoverage,
0 Timeout, 3 CompileError and 26 Ignored. All 16 critical fingerprints are Killed.
The complete command took 1,025.709 seconds; the engine reported 1,025.332 seconds.
This is 38.009 seconds above D's local 987.70-second sample, about 3.8%, and remains
within the unchanged 1,800-second budget. These are individual samples, not a
controlled causal performance comparison. The selected outcomes match D.
Stryker ran in its own worktree at the same commit to avoid shared build output.

The cost question raised during this increment is a follow-up performance trial,
not an unmeasured configuration change: benchmark 2 versus 4 workers (then 8 if
useful) with mixed mutants still disabled, compare all critical outcomes and
repeatability, and inspect slow tests/survivors. Stryker's
[since/baseline options](https://stryker-mutator.io/docs/stryker-net/configuration/)
can provide optional changed-only development feedback. They do not satisfy the
current fresh-source acceptance gate through cached results. G's mixed-mutant
and contention findings remain the reason not to raise parallelism or re-enable
mixing without a bounded comparison. No such tuning is included in P3-E.

## Retained artifacts

Only redundant Stryker HTML is omitted; raw JSON, TRX, logs, execution and source records remain.

- [diagnostics.zip](evidence/p3-e/diagnostics.zip), SHA-256 `07f5620e3c1438a91c2eb44603da7af56566a633134de7ad8a7687117ba126d5`.
- [host-qualification.zip](evidence/p3-e/host-qualification.zip), SHA-256 `b8ef2caa1e79d5ab0bb481ee5294b1f0bc8ada956424f65e2d2b9ec304660f6e`.
- [local-validation.zip](evidence/p3-e/local-validation.zip), SHA-256 `40a2835027eb6e6442018e027c715f66d47af0279ba9ee2348567611fde849ec`.

Complete local receipt SHA-256: `b3d4801617d738fccc00f60618183be2f8f775f6b43a0b84f6001c60725faec9`.
