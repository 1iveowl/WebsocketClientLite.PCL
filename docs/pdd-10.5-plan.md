# 10.5 plan: contract weakening, stated rules and a citation check

Written 2026-11-14. Release 10.5.0 changes no public API and no library behaviour. It fixes
three details the 10.4 .NET 11 bump missed, documents an experiment that weakens the header
length contract, writes down three rules the verification work already follows, and turns the
RFC citation habit into a check. The work is small topic branches into `releases/10.5.0`,
created from `main` at `d3308a0` (the merge of the 10.4.0 release, #163; the `10.4.0.0` tag is
on `8c5818a`), one pull request each. Each pull request is reviewed in a fresh session before
it merges. Releasing stays the owner's step.

| Step | Work | Branch | Needs |
| --- | --- | --- | --- |
| P0 | This plan | `docs/plan-10.5` | none |
| P1 | The README's .NET badge and ".NET 10 SDK" in the mutation and Stryker guides | `docs/net11-sdk-details` | none |
| P1b | `verification/exploration/run.py` starts the `net11.0` build | `fix/exploration-net11-build` | none |
| P2 | Contract-weakening experiment on `HeaderValid`, as below | `docs/contract-weakening` | none |
| P3 | Rules in `verification/README.md`, as below; pointers from `AGENTS.md` | `docs/verification-rules` | none |
| P4 | Citation check in `pdd.sh validate`, as below | `chore/pdd-citation-check` | P3 |
| R | Recount at the head of `releases/10.5.0` for the owner; release notes in `CHANGELOG.md` and the README's release history row | `docs/release-10.5.0` | P0 to P4 |
| M | **Owner releases**: `GitVersion.yml`, publish, release branch into `main` | | R |

**P2.** In `HeaderValid` (`verification/Protocol.dfy`), weaken one length bound at a time:
marker 126, `126 <= length <= 65535` becomes `0 <= length <= 65535`; marker 127,
`length >= 65536` becomes `length >= 0`. For each, record the verifier's last line, the
`scripts/dafny-masking.sh --check` result, the full test run after the C# is regenerated from
the weakened contract (`HeaderValid` is compiled into `verification/generated/Masking.g.cs`),
and the Dafny mutation run. The write-up is `verification/contract-weakening.md`; the patches,
commands and outputs are text files under `docs/evidence/contract-weakening/`, with the commit
they were run at. The weakened contract never reaches a pushed branch.

**P3.** Three standing rules in `verification/README.md`: the RFC wins when a requirement is
in question; tests take expected bytes from the RFC or write them out by hand, never from
production helpers (the plan rule in `docs/pdd-phase-3-plan.md`, lines 612 and 613); a new
test is seen to fail on the old code before the fix that makes it pass is trusted.
`AGENTS.md` points to these rules and to `verification/boundaries.json`.

**P4.** `pdd.sh validate` fails when a citation is missing. It reads two inputs:

- Each contract-bearing method, function, predicate and lemma in `verification/Protocol.dfy`
  and `verification/Masking.dfy` that Dafny proves. The comment block directly above it must
  name an RFC 6455 section (for example "RFC 6455 section 5.3", "Section 5.2:" or "sections
  5.2 and 5.5") or contain "Library policy:". A `{:extern}` declaration is not proved, but it
  is held to the same rule so that an assumption is never uncited.
- Each requirement's `origin` in `verification/requirements.json`. It must hold an RFC 6455
  section link (`https://www.rfc-editor.org/rfc/rfc6455.html#section-…`) or an entry that
  starts with "Library policy:".

A test keeps a contract without a citation as a failing case.

**R, the recount.** Read at the head of `releases/10.5.0`, each value with the file and line it
comes from, sent to the owner before the release pull request into `main` merges:

- the verifier's last line; lines of Dafny (`Protocol.dfy`, `Masking.dfy`), of generated C#,
  of library C# and tests, of `tools/Pdd` and its tests;
- requirements and obligations by kind; the number of `boundaries.json` entries;
- Dafny mutants and their timings; tests per target and the number of targets; line coverage;
- Stryker counts on the Close and handshake file, the must-kill count and the dispositions;
- the current line numbers of the `HeaderValid` RFC citation, `HeaderValid`, the length rule,
  `DecodeHeader`, the decoder's `status == 2` clause, `HeaderRoundTrip` and `Outgoing`; of the
  `81 7E 00 7D` row and `RejectsMalformedFrames` in `PayloadParsingTests.cs`; of the call to the
  decoder in `DataframeParsing.cs`; of the README sentence on stricter validation and the
  `verification/README.md` sentence on executable specifications;
- whether the body of `HeaderValid` is unchanged since `8f8f207`.

## Owner decisions (2026-11-14)

1. Release 10.4 is kept to the .NET 11 bump. The three details in P1 and P1b were missed by
   that bump and would have gone into it, but `releases/10.4.0.0` was published and merged into
   `main` before they were found, and CONTRIBUTING.md closes a release branch once it is
   published. They go into 10.5.
2. The next version is 10.5.0, set by the owner. It is a minor version, not 10.4.1, because
   P2 to P4 add a check to the evidence tooling and new verification documentation, which
   CONTRIBUTING.md treats as a feature, not as a fix to 10.4.0.
3. The release branch is named `releases/10.5.0`, the `releases/<x.y.z>` form AGENTS.md and
   CONTRIBUTING.md give. The last three release branches used four parts
   (`releases/10.4.0.0`).
4. P2 leaves the weakened contract out of the shipped code and out of the mutation catalog. A
   contract mutation that the tests catch, rather than the verifier, is a different kind of
   check; adding one to the mutation suite needs the owner's decision.
5. If no test catches a weakened bound in P2, `verification/contract-weakening.md` records it
   as an open gap, and the owner is told. No test is added for it without the owner's decision.
6. No repository setting changes in 10.5. Which status checks `main` requires is the owner's
   decision; R lists the candidates and the command.

## Facts checked on 2026-11-14

- `releases/10.5.0` and `main` were both `d3308a0` when the branch was created.
- The increment records under `verification/` (`parser-retention.md`,
  `queued-write-ownership.md`, `payload-lease-ownership.md`, `controlled-close.md`,
  `close-evidence-g.md`) describe their gates as they stood at their increment, with five
  targets and a .NET 10 host. They are left as they are.

## Lessons

Numbered as they are found, with the evidence.

1. **A target framework change must find hard-coded build paths.** The 10.4 bump (#159) moved
   `verification/exploration` to `net11.0`, but `run.py` and the replay command in
   `docs/p3-f-validation.md` still named `bin/Release/net10.0`, so the harness failed. Found in
   the review of #165, fixed in #166. Grep for `bin/Release/net` when a target changes.
2. **The mutation runner stops at the first mutant it does not detect.** Later mutants are
   recorded as `not-run` in `results.json`, so a run with one survivor says nothing about them.
   Under the weakened 16-bit contract, `HEADER-001-short-sixty-four-bit` was never run and had
   to be applied by hand (`verification/contract-weakening.md`, section d).
3. **Dafny mutant times vary by up to nine times on this host.** Most mutants take 8.5 to
   9.5 s (8.47 s at the fastest). With builds and tests running beside it, one took 77 s; in a rerun with nothing else
   from this work running, the baseline took 39 s and one mutant 76 s, against a 90 s budget
   (`docs/evidence/contract-weakening/marker126-4-mutations.txt` and
   `marker126-5-mutations-quiet.txt`). GitHub-hosted runners were about 2.4 times slower in
   10.3, so an outlier like that would be inconclusive there. Run mutation suites alone, and
   keep a slow run rather than rerunning it until it is fast.
