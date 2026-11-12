#!/usr/bin/env bash
# Second part of the S10 after-change checks, 2026-11-12: after-checks.sh was stopped by the
# session's two-hour background limit while its Stryker capture waited for the timing lock
# (it had not started). This runs that capture and aggregate.
set -u
repo=/home/vscode/worktrees/s10; out=/tmp/s10-after; cd $repo
echo "commit: $(git rev-parse HEAD)"; git status --short
/tmp/s10-scripts/timed.sh $out/capture-stryker ./scripts/pdd.sh capture --job stryker --output artifacts/pdd-s10/stryker; echo "capture stryker rc=$?"
./scripts/pdd.sh aggregate --output artifacts/pdd-s10 > $out/aggregate.log 2>&1; echo "aggregate rc=$?" | tee -a $out/aggregate.log
git status --short; echo ALL-DONE
