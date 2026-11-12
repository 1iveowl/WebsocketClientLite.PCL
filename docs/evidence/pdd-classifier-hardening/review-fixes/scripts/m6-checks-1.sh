#!/usr/bin/env bash
# M6 fix of #145, checks part 1 on the committed tree (2026-11-12). Each timed step takes the
# shared timing lock through timed.sh. Evidence is written outside the worktree first, so the
# tree stays clean for the capture jobs, which require an unchanged checkout.
set -u
repo=/home/vscode/worktrees/s10; out=/tmp/m6-out/after; mkdir -p $out; cd $repo
T=/tmp/m6-scripts/timed.sh
echo "commit: $(git rev-parse HEAD)"; git status --short
$T $out/pdd-tests dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release; echo "pdd tests rc=$?"
$T $out/review-findings-changed /tmp/m6-scripts/head-with-test.sh HEAD $repo/tools/Pdd.Tests/ClassifierHardeningTests.cs "FullyQualifiedName~ClassifierHardeningTests"; echo "review findings rc=$?"
$T $out/finding-7-shift-closestep /tmp/m6-scripts/shift-closestep.sh HEAD shift; echo "shift rc=$?"
./scripts/pdd.sh validate > $out/validate.log 2>&1; echo "validate rc=$?" | tee -a $out/validate.log
./scripts/dafny-masking.sh --check > $out/dafny-masking-check.log 2>&1; echo "dafny-masking --check rc=$?" | tee -a $out/dafny-masking-check.log
rm -rf artifacts/m6-mutations
$T $out/mutations ./scripts/pdd.sh mutations --output artifacts/m6-mutations; echo "mutations rc=$?"
rm -rf artifacts/pdd-m6
$T $out/capture-protocol ./scripts/pdd.sh capture --job protocol --output artifacts/pdd-m6/protocol; echo "capture protocol rc=$?"
git status --short; echo "commit after: $(git rev-parse HEAD)"
echo PART-1-DONE
