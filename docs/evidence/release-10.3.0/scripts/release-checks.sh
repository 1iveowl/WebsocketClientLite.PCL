#!/usr/bin/env bash
# S9 release-readiness checks on the tip of releases/10.3.0.0 (2026-11-12).
# Timed steps take the shared timing lock through timed.sh. Kept evidence is written outside
# the worktree (/tmp/s9-out) so the tree stays clean for the capture jobs.
set -u
repo=/home/vscode/worktrees/s9; out=/tmp/s9-out; mkdir -p $out; cd $repo
T=/tmp/s9-scripts/timed.sh
echo "commit: $(git rev-parse HEAD)  tip of origin/releases/10.3.0.0: $(git rev-parse origin/releases/10.3.0.0)"; git status --short
echo "build:"; dotnet build src/main/WebsocketClientLite.sln -c Release > $out/build.log 2>&1; echo "build rc=$?" | tee -a $out/build.log
rm -rf artifacts/s9-coverage
$T $out/test-all-tfms ./test-all-tfms.sh Release; echo "test-all-tfms rc=$?"
$T $out/coverage dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/s9-coverage; echo "coverage run rc=$?"
./scripts/check-coverage.sh artifacts/s9-coverage 70 > $out/coverage-gate.log 2>&1; echo "coverage gate rc=$?" | tee -a $out/coverage-gate.log
$T $out/pdd-tests dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release; echo "pdd tests rc=$?"
./scripts/pdd.sh validate > $out/validate.log 2>&1; echo "validate rc=$?" | tee -a $out/validate.log
./scripts/dafny-masking.sh --check > $out/dafny-masking-check.log 2>&1; echo "dafny-masking --check rc=$?" | tee -a $out/dafny-masking-check.log
rm -rf artifacts/s9-mutations
$T $out/mutations ./scripts/pdd.sh mutations --output artifacts/s9-mutations; echo "mutations rc=$?"
rm -rf artifacts/pdd-s9
for job in protocol tests controlled stryker; do
  $T $out/capture-$job ./scripts/pdd.sh capture --job $job --output artifacts/pdd-s9/$job; echo "capture $job rc=$?"
done
./scripts/pdd.sh aggregate --output artifacts/pdd-s9 > $out/aggregate.log 2>&1; echo "aggregate rc=$?" | tee -a $out/aggregate.log
git status --short; echo "commit after: $(git rev-parse HEAD)"
echo ALL-DONE
