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
