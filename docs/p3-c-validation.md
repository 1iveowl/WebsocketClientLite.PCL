# P3-C validation and review record

P3-C only, from release 77af6c570d0f52e6a6c31a85116cf687a99c53b1 (merged G/A/B).
The specification was read at planning commit 78966de; its worktree was not changed.
See [parser ownership and exclusions](../verification/parser-retention.md).

The retained G baseline archive and receipt were checked: SHA-256
`6701d1d713c8f4b9ec85981fbeaefd0eeddc4cbb348a642071ce628a83f47065` and
`1d7b19e709f7a2dcc8cfb4e50baa6aacd40c8c4fe42772ff8bb914f770fc4756` respectively.
Its machine gate passed; historical F failure is not relabeled. G/A validation
records and B's merged changes remain inherited, not new P3-C claims.

## Genuine defects and production scope

No genuine production defect was reproduced. No production file, public API,
BlockCopy implementation, Dafny/generated source, F native/masking test, existing
budget, GitVersion.yml or other session was changed. The original core.12592
was not read, copied or modified. The two artificial defects below are isolated
evidence checks, not shipping bug reports. No later increment or publication is
included. The owner explicitly requested merge into the release after completion;
human semantic review is still distinct from a machine receipt.

## Final local evidence

Checked clean commit: `03c2519`. All three captures have identical relevant source
hashes before/after and identify that commit. Later documentation/archive commits
do not relabel it; final PR CI binds its own source and merge revision.

Host: Linux x86-64 Debian container, kernel 7.2.11+deb13-amd64, SDK 10.0.403,
actual test runtime .NET 10.0.14. The five library TFMs are not five runtime hosts.
Package/tool caches were warm; isolated defect copies were fresh. Protocol, tests
and controlled jobs overlapped. No other session was controlled. Contention,
peak RSS, allocation benchmarks and monetary billing were not measured.

| Check | Result | Seconds |
| --- | --- | ---: |
| Release solution build | pass, zero warnings/errors | 2.88 |
| Five-target Release matrix | 260 tests per target, all pass | 134.85 |
| Coverage | 82.31% lines, 77.11% branches | 26.98 |
| Unchanged 70% line gate | pass | 0.01 |
| Tooling fixtures | 200 pass | 10.34 |
| Pinned proof, trust audit, generated comparison | pass | 11.06 |
| Dafny baseline plus all 15 mutations | baseline pass, all detected | 146.01 |
| Inherited ordering experiment | detected | 5.75 |
| Inherited G boundary experiments | all 13 detected | 25.16 |
| Inherited A queued overlap | detected | 4.13 |
| Inherited B lease/count experiments | all three detected | 12.30 |
| New control-total and late-scratch experiments | both named assertions detected | 5.58 |

The 65 new matrix case executions sum to 0.1825 seconds; maximum 0.0252 seconds.
One case enumerates 54 fragment combinations; this is bounded enumeration, not
an arbitrary-sequence proof. Timings are not a controlled estimate of added matrix
wall time. G's 149.49-second CI matrix is a different environment and cannot
establish a performance improvement. The 5.58-second added controlled command
fits the provisional 120-second allowance; no budget was raised. Added maintained
code is 229 test lines, 121 runner lines and 80 fixture lines, plus small existing
manifest/aggregate hooks. No new package, project or proof model was introduced.
Review future normal CI samples before claiming recurring medians or tail cost.

The unchanged Stryker gate and complete four-job aggregate are required in PR CI;
this local capture does not claim a fresh Stryker run or complete aggregate.
All 16 critical fingerprints remain required. Human-review obligations remain
unsatisfied by machine results.

[Local capture](evidence/p3-c/local-validation.zip), SHA-256
`08baaefc13c2987796e5da29f978fa2fc29488073b3a15577cc782f3183cf9aa`,
contains raw TRX/logs, source/runtime identities, execution records, coverage and
proof/mutation evidence. Both new experiments first pass all 13 adapter cases
plus the runtime probe; mutations then fail exactly their named assertions.
The aggregate requires them under REASSEMBLY-001-TEST without changing its proof.

## Diagnostic attempts

The first 12 targeted test cases passed. The first isolated experiment also
passed its assertions, but review found a scripted rename had changed `Release`
to `Reretention` in its command. That run is diagnostic-only, not Release evidence.
The configuration was corrected and a strict Release/net10.0 check plus a negative
configuration fixture added. The subsequent dirty-source Release experiment
passed, followed by the final clean capture above. The initial tooling run passed
199 cases; the final run includes the additional configuration rejection (200).
No timeout, compilation failure or unrelated assertion was counted as detection.

[Diagnostics](evidence/p3-c/diagnostics.zip), SHA-256
`d3b219de9cfb7d9623de0d620ac4e9a360ee6c0e5a3d2f7fe77bfecf2c317cd2`.

## Review limits

Tests observe retained totals through emitted bytes/metadata and admission/read
budgets; they do not inspect every internal local assignment. Single-reader
ownership, stable payload storage, stream contracts, Rx/runtime/compiler and
native copy remain trusted. Byte limits do not bound fragment/object counts or
process memory. Cancellation cases inject the transport cancellation outcome;
no exhaustive subscription-disposal scheduling claim is made. Revisit these
cases if scratch ownership, parser fast paths or accumulated-limit policy changes.
