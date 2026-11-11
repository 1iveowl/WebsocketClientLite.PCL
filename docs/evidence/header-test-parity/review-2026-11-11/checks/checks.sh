#!/usr/bin/env bash
# Checks of the #140 review fixes, 2026-11-11. Heavy steps run under the shared timing lock
# (flock -o, so that build servers started by a step do not inherit the lock). Each step
# records the load and the other dotnet/dafny/stryker processes when it starts.
cd /home/vscode/worktrees/fix-140
L=artifacts/review-checks
if [ "${1:-}" = run ]; then
  name=$2; lock=$3; shift 3; s=$(date +%s); case "$lock" in [0-9]*) lock="yes, waited $(( s - lock ))s";; esac
  echo "== $name start $(date -u +%FT%TZ) commit $(git rev-parse HEAD) dirty=$(git status --porcelain | wc -l) lock=$lock"
  { uptime; ps -eo pid,etime,args --sort=start_time | grep -E "dotnet|dafny|[Ss]tryker" | grep -v -E "grep|fix-140" | cut -c1-160; } > $L/$name.others.txt 2>&1
  "$@" > $L/$name.log 2>&1; c=$?
  echo "exit=$c seconds=$(( $(date +%s) - s ))" > $L/$name.exit; echo "== $name exit=$c seconds=$(( $(date +%s) - s ))"
  exit 0
fi
mkdir -p $L; rm -rf artifacts/pdd-review artifacts/protocol-coverage-review
plain() { "$0" run "$1" no "${@:2}"; }
locked() { w=$(date +%s); flock -o ~/worktrees/.timing.lock "$0" run "$1" "$w" "${@:2}"; }
plain build dotnet build src/main/WebsocketClientLite.sln -c Release
plain pdd-validate ./scripts/pdd.sh validate
plain pdd-tests dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release
locked test-all-tfms ./test-all-tfms.sh Release
locked coverage-tests dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/protocol-coverage-review
locked coverage-gate ./scripts/check-coverage.sh artifacts/protocol-coverage-review 70
locked capture-protocol ./scripts/pdd.sh capture --job protocol --output artifacts/pdd-review/protocol
locked capture-tests ./scripts/pdd.sh capture --job tests --output artifacts/pdd-review/tests
locked capture-controlled ./scripts/pdd.sh capture --job controlled --output artifacts/pdd-review/controlled
locked capture-stryker ./scripts/pdd.sh capture --job stryker --output artifacts/pdd-review/stryker
plain aggregate ./scripts/pdd.sh aggregate --output artifacts/pdd-review
echo "== final dirty=$(git status --porcelain | wc -l)"
echo CHECKS-DONE
