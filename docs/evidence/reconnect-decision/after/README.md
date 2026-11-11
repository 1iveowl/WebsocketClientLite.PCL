# Evidence: Experiment 1 after numbers (S6, 2026-11-11)

Made on 2026-11-11 in step S6 of `docs/pdd-10.3-session-plan.md`. The production
change is commit `bd743b3` (`ReconnectDecision.Decide`, the shell change in
`ClientWebSocketRx.cs` and the plain tests `ReconnectDecisionTests`). The base is
`fa96d59`, as in S5. The results and the recommendation are in
`verification/reconnect-decision.md`, section "Results and recommendation".

Every run here is a local measurement on one host: Linux 7.2.11, AMD Ryzen 9
9955HX, 32 logical CPUs, .NET SDK 10.0.403, test host .NET 10.0.14. Other step
sessions ran on the machine at the same time (controller note of 2026-11-11). Every
timed run reported below held the shared timing lock `~/worktrees/.timing.lock`, and
`processes.txt` beside it lists what else was running before each timed command.
Every run used a `git archive` copy or a clean worktree, never the working checkout
with changes. `commands.json` lists each run, including the failed and interrupted
attempts, which were kept and not retried into a pass.

## Contents

- `timing/`: the reconnect suite's run times.
  - `measure.sh` is S5's `baseline/measure.sh` with three additions, listed at its
    top: a process list before each timed command, the tree hash, and three runs of
    the plain tests where they exist. S5's commands are unchanged.
  - `run-locked.sh` extracts `git archive` copies of `bd743b3` and `fa96d59`, commits
    each into a scratch repository (`pdd.sh reconnect-defect` needs `git ls-files`)
    and checks that the scratch tree hash equals the commit's tree hash
    (`copies.txt`). Under one hold of the lock it then runs `measure.sh` on the after
    copy and, right after it, on the base copy (controller note, item 6).
  - `after/` and `base-rerun/`: `timings.txt`, `host.txt`, `processes.txt`, the logs,
    the TRX files, `case-durations.txt` (S5's `trx_cases.py`) and the
    `reconnect-defect` report. `lock.txt` has the lock times.
  - `failed-attempts/`: two attempts that stopped before the lock was taken, so they
    measured nothing: the scratch commit had no git identity, then `git add -A` left
    out tracked files that `.gitignore` matches, so the tree check failed.
- `decision-defects/`: seeded defects in the decision lines, recorded both ways as
  S5's decision 6 suggests (controller note, item 6).
  - `catalog-before.json`: six defects in `ClientWebSocketRx.cs` at `fa96d59`, each
    named by the transition-table rows it changes.
  - `catalog-after.json`: the same six defects seeded in `ReconnectDecision.Decide`
    (side `after-function`), and seven defects on the shell's side of the same
    lines at `bd743b3`, where the function is right and the shell ignores or misuses
    what it returns (side `after-shell`).
  - `decision_defects.py`: builds the copy, runs the unchanged copy and then each
    defect: the reconnect suite three times, the other existing tests once and the
    plain tests once. It applies each edit exactly once and restores the file.
    Detection runs are not timed and did not take the lock.
  - `before/`, `after-function/`, `after-shell/`: `results.json`, one `.diff` per
    defect, and `raw.zip` with every log and TRX file.
  - `summary.md`: the table, made by `summarize.py`.
  - `interrupted/`: two after-function attempts stopped by the session before any
    defect was applied, with the reason in each `INTERRUPTED.txt`.
- `count_region_after.py` and `region-counts.txt`: the stateful region of
  `ClientWebSocketRx.cs` at `bd743b3`, counted as S5's `count_region.py` counts it, and
  the line counts of the two new files. Both scripts are kept unchanged as the ones
  that made the recorded counts; later counts use `../region/count_region.py`, which
  holds the rules once and reproduces both (see "Changes after the M3 review").
- `checks/`: the four `pdd.sh capture` jobs (protocol, tests, controlled, stryker)
  at `bd743b3` from a clean worktree, then `aggregate`. `capture-run.log` has the
  times and the commit. `capture-bd743b3.zip` holds the capture directories,
  `summary.md` is a copy of the aggregate receipt summary `pdd/summary.md` from that
  archive, with its links turned into plain paths inside the archive, `mutant-times.txt` lists each Dafny
  mutant's time against its budget, and `*.console.log` are the jobs' console
  output. `run-captures.sh` ran them. The protocol job held the lock, because its
  per-mutant times are reported; `processes-before-protocol.txt` lists what else was
  running. The tests, controlled and stryker jobs are untimed checks and did not.

## Changes after the M3 review (2026-11-11)

The `/code-review high` of PR #142 before merge point M3 found the following; this
section records what was changed in this folder and why.

- **Logs force-added (findings 1 and 2).** `.gitignore` matches `*.log`, so none of the
  `.log` files this README and `commands.json` cite had been committed. They were
  force-added unchanged (`git add -f`), 51 files: `checks/capture-run.log` and the five
  `checks/*.console.log`; every per-command log in `timing/after/` and
  `timing/base-rerun/`, including the `reconnect-defect` `output.log`, `stdout.log` and
  `stderr.log`; `timing/console.log`; both failed timing attempts,
  `timing/failed-attempts/attempt-1-console.log` (exit 128, no git identity) and
  `attempt-2-console.log` (exit 1, tree mismatch); `decision-defects/after-console.log`,
  `decision-defects/before/console.log`, and the `unchanged/` logs of both interrupted
  attempts. Every cited file existed on disk, so none is missing and none was
  recreated.
- **Process lists reduced.** On 2026-11-11 the process lists were reduced to process
  names because they exposed other sessions' prompts. Each process line in
  `checks/processes-before-protocol.txt`, `timing/after/processes.txt` and
  `timing/base-rerun/processes.txt` now holds only its pid, its elapsed time and the
  command name: the executable's basename, and for `dotnet` the assembly or subcommand
  name. Timings and every other field are unchanged; the `==` header lines with their
  times and load averages are as recorded. `timing/measure.sh` and
  `checks/run-captures.sh` still show the `ps -eo pid,etime,args` command they ran,
  unchanged, because they are the commands, not their output. The logs force-added
  above were scanned the same way and hold no process lines.
- **Links in `checks/summary.md` (finding 8).** Its links pointed at capture
  directories that exist only inside `capture-bd743b3.zip`, under `pdd/`. They are now
  plain paths into the archive. The generated file is unchanged in the archive.
- **`decision-defects/decision_defects.py` (finding 9).** A timeout now kills the
  command's whole process group, and a skipped test is no longer counted as a failure.
  The recorded results were made by the earlier version; none of their runs timed
  out or had a skipped test, so no recorded result changes. The script's docstring
  says so.
- **Region counts (finding 10).** `count_region_after.py` was a copy of S5's
  `count_region.py` with other anchors. `../region/` now holds one script with the
  rules and one spec per commit; it reproduces both recorded counts exactly (see
  `../region/README.md`).
- The timing conclusion in `verification/reconnect-decision.md` was corrected to the
  real largest differences (finding 3). The tip checks are kept in
  `../tip-checks/2026-11-11/` (finding 4).
