# Migration reference fixtures

These are data captured from the Python tools at release commit
`a51f52f4aec72d71de36cf516e1845b26de1ccc1` before D2 removed those tools.
They require no Python at test or runtime.

- `evidence/` is the successful synthetic receipt corpus from the original
  receipt tests: job JSON, named-test TRX, logs, mutation outcomes and coverage.
- `identity.json` is that corpus's synthetic source identity.
- `receipt-success.expected.json` and each `receipt-cases.json` expected value
  are Python-produced semantic projections. Cases mutate one copy of the same
  corpus. They cover missing/malformed/interrupted/stale/mismatched evidence.
- `mutation-cases.json` preserves Python classifier inputs and expected outcomes,
  including the observed per-obligation timeout diagnostic.

Historical commands and temporary paths in the corpus are metadata, not commands
to execute. Tests bind CI identity to the current run. Receipt comparisons ignore
only timestamps, path-dependent text, command spelling and diagnostic wording;
they compare requirement/obligation statuses, accepted/rejected jobs, matrix test
outcomes, actual runtime, review, Stryker and coverage status. One deliberate
safety correction is called out in `ReceiptTests`: a proof claiming `passed`
with a nonzero exit now also fails the obligation, not only the overall gate.

The full live receipt from D2's local pipeline was also compared with Python's
aggregation of the identical artifacts: every JSON value matched after removing
`created_at`. Python independently classified the real baseline and all five
mutants identically. The prior 38 Python tests passed before removal. The C# suite
retains the failure modes and adds actual descendant/session cancellation and
additional malformed-evidence checks.

## Line numbers updated on 2026-11-11

Commit `ba499c3` added RFC citation comments to `verification/Protocol.dfy`, which
moved the lines these fixtures name. On 2026-11-11 (pull request #136, owner
decision on Q2):

- `payload-diagnostics.json` was replaced with the observed output of a full
  mutation run on the current source, `docs/evidence/header-traceability/mutations-2`.
  It now also holds the two `HEADER-001` mutants.
- In `mutation-cases.json`, only the Protocol.dfy and Masking.dfy line numbers
  were shifted to the current `CloseStep` lines (146→199, 147→200, 153→206,
  170→228, 317→389). The cases and their expected outcomes are unchanged. They
  remain Python-produced classifier inputs, not a new Python run.

## Lines relative to declarations since 2026-11-11

The 2026-11-11 shift above had to be redone by hand because the fixtures named absolute
lines. Since the S10 change of the 10.3 session plan (the #136 review, finding 7), no
fixture names an absolute `Protocol.dfy` or `Masking.dfy` line. `mutation-cases.json` and
`payload-diagnostics.json` write each line as `{Module.Class.Member+N}` or
`{Module.Class.Member-N}`: N lines after or before the line of the opening brace of that
member's body. `tools/Pdd.Tests/Fixtures.cs` finds that line in the current source with
the declaration lookup the classifier uses (`Requirements.DafnyMember`) and replaces each
anchor before a test reads the record. `MutationTests` builds its synthetic records the same
way. Each anchor names the declaration that encloses the line. The one exception is the
`Masking.dfy` case of `mutation-cases.json`, which copies the `CloseStep` lines of the
target case and changes only the file name, so it keeps `CloseStep` anchors.

The conversion changed only the line numbers, in the locations and in the source excerpts
of the observed output. Rendered against the source at `29697a4`, both files are equal to
what they were before. The conversion and that check are in
`docs/evidence/pdd-classifier-hardening/`. A comment line added above `CloseStep` now leaves
the Pdd tests passing; before the change, it made 11 of them fail.

On 2026-11-12, after the review of pull request #145, the anchors are rendered against the
source each record is classified against: for an observed mutant run in
`payload-diagnostics.json`, the source with that mutant applied, and for `mutation-cases.json`,
the unchanged source. The lookup uses the classifier's declaration kinds
(`Mutations.MethodKinds` and `Mutations.FunctionKinds`), and it finds a body by the layout rule
the clause reader relies on, not by guessing which braces are set displays. On today's
`Protocol.dfy` and `Masking.dfy` every declaration has the same body line as before
(`docs/evidence/pdd-classifier-hardening/review-fixes/`), so the rendered fixtures are unchanged.
