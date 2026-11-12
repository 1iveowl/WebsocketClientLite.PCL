#!/usr/bin/env bash
# S10 after-change checks on the committed tree (2026-11-11). Each timed step takes the
# shared timing lock through timed.sh. Kept evidence is written outside the worktree first,
# so the tree stays clean for the capture jobs, which require an unchanged checkout.
set -u
repo=/home/vscode/worktrees/s10
out=/tmp/s10-after; mkdir -p $out
cd $repo
T=/tmp/s10-scripts/timed.sh
echo "commit: $(git rev-parse HEAD)"; git status --short
$T $out/finding-7-shift-closestep /tmp/s10-scripts/shift-closestep.sh HEAD shift; echo "finding-7 shift rc=$?"
$T $out/findings-4-5-changed python3 -I /tmp/s10-scripts/experiment.py $repo HEAD --filter "FullyQualifiedName~ClassifierHardeningTests|FullyQualifiedName~PropositionLocationMustLieInAPredicateTheTargetNames"; echo "findings 4-6 rc=$?"
./scripts/pdd.sh validate > $out/validate.log 2>&1; echo "validate rc=$?" | tee -a $out/validate.log
./scripts/dafny-masking.sh --check > $out/dafny-masking-check.log 2>&1; echo "dafny-masking --check rc=$?" | tee -a $out/dafny-masking-check.log
rm -rf artifacts/s10-after-mutations
$T $out/mutations ./scripts/pdd.sh mutations --output artifacts/s10-after-mutations; echo "mutations rc=$?"
rm -rf artifacts/pdd-s10
for job in protocol tests controlled stryker; do
  $T $out/capture-$job ./scripts/pdd.sh capture --job $job --output artifacts/pdd-s10/$job; echo "capture $job rc=$?"
done
./scripts/pdd.sh aggregate --output artifacts/pdd-s10 > $out/aggregate.log 2>&1; echo "aggregate rc=$?" | tee -a $out/aggregate.log
git status --short; echo "commit after: $(git rev-parse HEAD)"
echo ALL-DONE
