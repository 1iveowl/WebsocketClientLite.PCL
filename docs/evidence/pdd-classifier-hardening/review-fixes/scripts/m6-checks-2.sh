#!/usr/bin/env bash
# M6 fix of #145, checks part 2 (2026-11-12): the tests, controlled and stryker captures and
# aggregate, into the capture directory of part 1.
set -u
repo=/home/vscode/worktrees/s10; out=/tmp/m6-out/after; cd $repo
T=/tmp/m6-scripts/timed.sh
echo "commit: $(git rev-parse HEAD)"; git status --short
for job in tests controlled stryker; do
  $T $out/capture-$job ./scripts/pdd.sh capture --job $job --output artifacts/pdd-m6/$job; echo "capture $job rc=$?"
done
./scripts/pdd.sh aggregate --output artifacts/pdd-m6 > $out/aggregate.log 2>&1; echo "aggregate rc=$?" | tee -a $out/aggregate.log
git status --short; echo "commit after: $(git rev-parse HEAD)"
echo PART-2-DONE
