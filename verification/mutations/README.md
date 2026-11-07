# Close negative proof checks

Run `python3 scripts/run-close-mutations.py` from any directory. Python 3.9+,
Linux x64, curl and unzip are required. `scripts/dafny-toolchain.sh` shares the
existing checksum-pinned Dafny 4.11.0 / Z3 4.12.1 bootstrap with generation; the
runner checks versions and explicitly selects that solver. It does not use a
verifier from PATH. No public library API or proof contract changes here.

`close.json` is the version-1 catalog. Each entry has a unique ID, requirement
and mutation obligation from `requirements.json`, qualified method, exact
single-line body edit, target postcondition, allowed companion postconditions
and diagnostic classification. The runner validates these links and rejects
missing, ambiguous or out-of-body edits before running the toolchain. Every
existing or increment-B mutation obligation must have a catalog entry. Changing
the chosen behaviors still needs semantic review; this structural check cannot
judge whether an edit meaningfully covers an obligation.

| Mutant | Requirement | Intended defect |
| --- | --- | --- |
| `CLOSE-001-duplicate-send` | CLOSE-001 | Ignore an existing send reservation |
| `CLOSE-001-suppressed-send` | CLOSE-001 | Suppress an eligible send |
| `CLOSE-002-state-bit-loss` | CLOSE-002 | Reset prior flags when recording receipt |
| `CLOSE-003-premature-release` | CLOSE-003 | Release unconditionally |
| `CLOSE-003-suppressed-release` | CLOSE-003 | Suppress an eligible release |

All five are non-equivalent under the current preconditions; there are no
coverage exceptions. The send and release biconditionals are exercised in both
directions, so rejecting every input cannot pass these acceptance checks. This
does not prove that the requirements or catalog are complete. Equivalent mutants
must be documented and reviewed, never counted as detected.

The baseline verifies `Masking.dfy` including `Protocol.dfy`. Each experiment
starts from the same in-memory source snapshot in its own temporary directory.
No repository-wide copy (and no core dumps, generated C# or packages) is needed.
Edits do not change line counts; diagnostic locations resolve back to exact
catalogued clauses rather than treating line numbers as stable identities.

Detection requires Dafny exit code 4, a completed nonzero-error verification
summary, and only postcondition errors at `CloseStep` with related locations at
the allowed clauses. At least one must match the intended target clause. Any
additional failure, malformed/unknown diagnostic, parse error, zero verified
baseline, missing tool, application error or exhausted budget fails the
experiment as inconclusive. A completed successful mutant is reported as
`survived`. No later mutant runs after a failure; remaining entries are `not-run`.
The text parser is deliberately specific to pinned Dafny 4.11.0 diagnostics and
must be reviewed alongside any future toolchain upgrade.

Each fresh evidence directory contains:

- `results.json`: baseline and experiment status, source SHA-256 hashes,
  timestamps, elapsed times, budgets, commands, stdout/stderr, exit codes and
  matched clauses.
- Separate stdout/stderr logs for bootstrap, version probes and each experiment.
- `tool-versions.json`, copies of the catalog and requirement manifest, and the
  original Dafny source snapshots used by the run.
- `failure.json` when setup, edit application or interruption prevents normal
  completion. A partial `results.json` remains when the suite has started.

`--output PATH` requires a nonexistent directory; the default creates a unique
UTC-named directory under `artifacts/close-mutations`. Stale results cannot pass
a new run. Both SIGINT and SIGTERM stop the verifier process group and unwind the
temporary source directory; process timeouts also kill the group. SIGKILL or
machine loss cannot run cleanup, but temporary directories are outside the
checkout and are never reused. The process tests exercise timeout, missing tools,
interruption and cleanup; classifier tests cover survival and unrelated errors.

Local measurement on 2026-11-07: baseline 9.785 seconds, mutants 8.930–9.937
seconds each, 57.004 seconds total verification time, excluding bootstrap/version
probes. Defaults are provisional: 180 seconds for the baseline, 90 per mutant,
60 per Dafny obligation, two verifier cores. Override wall-clock budgets with
`--baseline-budget` and `--mutant-budget`; exhaustion is explicitly recorded and
never a detection. CI uploads actual elapsed times on every PR. CI measurements
now include an 18.317-second successful baseline and a later 37.153-second
inconclusive baseline on identical source hashes: `HeaderRoundTrip` exceeded
the original 30-second per-obligation limit. See the [successful PR run](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42386797831)
and [failed post-merge run](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42387119885).
The default per-obligation limit is increased to 60 seconds to allow more
headroom, independently of the unchanged wall-clock limits. Override it with
`--verification-time-limit SECONDS` (positive integer); the exact value is
recorded in each experiment's command. Larger values do not extend the outer
wall-clock budget. This mitigates timing sensitivity without establishing its
cause or guaranteeing future completion. No automatic retries, contract
changes, skipped proofs or relaxed diagnostic classification are introduced.
Review these budgets against further runs before expanding the suite.
The small Close suite remains a PR gate; no
scheduled-only replacement is introduced.

Full requirement receipts and cross-job source identity checks belong to
increment D. Controlled asynchronous Close schedules belong to increment E.
