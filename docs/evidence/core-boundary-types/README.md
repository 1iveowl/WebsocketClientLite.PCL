# Evidence for Experiment 2: named types at the boundary of the verified core

Steps S7 and S8 of `docs/pdd-10.3-session-plan.md`, and the M4 review fixes. The
report is [`verification/core-boundary-types.md`](../../../verification/core-boundary-types.md).

- `baseline/`: S7, before any code change (base commit `fa96d59`).
- `after/`: S8, on the production commit `13ed44f`. `after/superseded-6646466/` holds
  the runs on an earlier version of that commit.
- `decision-path/`: S8's evidence-only probe of the per-frame receive and send paths.
- `after-review/`: the M4 review fixes, 2026-11-11 and 2026-11-12: benchmark, probe and
  seeded defects on `3519275`, and every check on `7415369` (`after-review/checks/`).

## Process lists reduced to process names (2026-11-11)

On 2026-11-11 the process lists in every `.meta` file under this directory were
reduced to process names, because they exposed other sessions' prompts. The S7 and S8
runs had recorded `ps -eo pid,etime,args | grep -E "claude -p|dotnet" | grep -v grep`,
whose full command lines held the prompts of other headless sessions, shell snapshot
command lines and local paths. Each process line now keeps only its pid, its etime and
the command name: the executable's basename, and for `dotnet` the subcommand or the
assembly name (for example `dotnet test`, `MSBuild.dll`, `testhost.dll`). The timings
and every other field are unchanged, including the header line that names the command
that was run. `bench.sh` and `decision-path/run.sh` record
`ps -eo pid,etime,comm | grep -E "claude|dotnet|VBCSCompiler"` from then on.

The earlier text stays in the history of the branch `chore/core-boundary-types`; it was
not rewritten. A claim in the report that rests on the full command lines, such as which
session another `claude` process was, can no longer be checked from these files.
