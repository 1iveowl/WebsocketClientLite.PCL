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
  the line counts of the two new files.
- `checks/`: the four `pdd.sh capture` jobs (protocol, tests, controlled, stryker)
  at `bd743b3` from a clean worktree, then `aggregate`. `capture-run.log` has the
  times and the commit. `capture-bd743b3.zip` holds the capture directories,
  `summary.md` is the aggregate receipt summary, `mutant-times.txt` lists each Dafny
  mutant's time against its budget, and `*.console.log` are the jobs' console
  output. `run-captures.sh` ran them. The protocol job held the lock, because its
  per-mutant times are reported; `processes-before-protocol.txt` lists what else was
  running. The tests, controlled and stryker jobs are untimed checks and did not.
