# 10.5 plan: contract weakening, stated rules and a citation check

Written 2026-11-14. Release 10.5.0 changes no public API and no library behaviour. It
documents an experiment on the header length contract, writes down three rules the
verification work already follows, and turns the RFC citation habit into a check. The work
is small topic branches into `releases/10.5.0`, created from `main` at `d3308a0` (the
10.4.0 release), one pull request each. Each pull request is reviewed in a fresh session
before it merges. Releasing stays the owner's step.

| Step | Work | Branch | Needs |
| --- | --- | --- | --- |
| P0 | This plan | `docs/plan-10.5` | none |
| P1 | Two .NET 11 SDK details the 10.4 bump missed: the README's .NET badge and ".NET 10 SDK" in the mutation and Stryker guides | `docs/net11-sdk-details` | P0 |
| P2 | Contract-weakening experiment on `HeaderValid`, both length branches, with outputs under `docs/evidence/` | `docs/contract-weakening` | P0 |
| P3 | Rules in `verification/README.md`: the RFC wins, expected bytes from the RFC, see the new test fail; pointers from `AGENTS.md` | `docs/verification-rules` | P0 |
| P4 | `pdd.sh validate` fails on a proved declaration or requirement origin without an RFC section or "Library policy:" | `chore/pdd-citation-check` | P3 |
| R | Recount at the head of `releases/10.5.0` for the owner, then the owner sets `GitVersion.yml` and releases | | P1 to P4 |

## Owner decisions (2026-11-14)

1. Release 10.4 is the .NET 11 bump only. The two stale .NET details belong to it, but
   `releases/10.4.0.0` was published and merged into `main` before they were found, and
   CONTRIBUTING.md closes a release branch once it is published. They are step P1 here.
2. The release branch is named `releases/10.5.0`, the `releases/<x.y.z>` form AGENTS.md
   and CONTRIBUTING.md give.
3. P2 leaves the weakened contract out of the shipped code and out of the mutation catalog.
   A contract mutation that the tests catch, rather than the verifier, is a different kind of
   check; adding one to the mutation suite needs the owner's decision.
4. If no test catches a weakened branch in P2, it is written down as an open gap. No test is
   added for it in this release without the owner's decision.
5. No repository setting changes. Which checks `main` requires is the owner's decision.

## Lessons

Numbered as they are found, with the evidence.
