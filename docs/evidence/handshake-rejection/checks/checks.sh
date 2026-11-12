#!/usr/bin/env bash
# Checks of S11 (rejected-handshake test), 2026-11-11. Heavy steps run under the shared
# timing lock (flock -o, so that build servers started by a step do not inherit the lock),
# one lock hold per step. Each step records the process list (names only) at its start
# and its end, and how long it waited for the lock.
# Review fix 2026-11-12 (#144 finding 7): each step returns its exit code, and the driver
# stops at the first step that fails, prints CHECKS-STOPPED with its name and code, and
# exits with that code. CHECKS-DONE is printed only when every step passed.
# TAG names the output directories (default s11: artifacts/s11-checks, pdd-s11, coverage-s11).
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
T=${TAG:-s11}
L=artifacts/$T-checks
procs() { ps -eo pid,etime,comm | grep -E 'claude|dotnet' | grep -v grep; }
if [ "${1:-}" = run ]; then
  name=$2; lock=$3; shift 3; s=$(date +%s); case "$lock" in [0-9]*) lock="yes, waited $(( s - lock ))s";; esac
  echo "== $name start $(date -u +%FT%TZ) commit $(git rev-parse HEAD) dirty=$(git status --porcelain | wc -l) lock=$lock"
  { echo "# start $(date -u +%FT%TZ) lock=$lock"; uptime; procs; } > $L/$name.processes.txt 2>&1
  "$@" > $L/$name.log 2>&1; c=$?
  { echo "# end $(date -u +%FT%TZ)"; uptime; procs; } >> $L/$name.processes.txt 2>&1
  echo "exit=$c seconds=$(( $(date +%s) - s ))" > $L/$name.exit; echo "== $name exit=$c seconds=$(( $(date +%s) - s ))"
  exit $c
fi
mkdir -p $L; rm -rf artifacts/pdd-$T artifacts/coverage-$T
stop() { echo "== final dirty=$(git status --porcelain | wc -l)"; echo "CHECKS-STOPPED step=$1 exit=$2"; exit "$2"; }
plain() { "$0" run "$1" no "${@:2}" || stop "$1" $?; }
locked() { w=$(date +%s); flock -o ~/worktrees/.timing.lock "$0" run "$1" "$w" "${@:2}" || stop "$1" $?; }
plain build dotnet build src/main/WebsocketClientLite.sln -c Release
plain pdd-validate ./scripts/pdd.sh validate
plain pdd-tests dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release
locked test-all-tfms ./test-all-tfms.sh Release
locked coverage-tests dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/coverage-$T
plain coverage-gate ./scripts/check-coverage.sh artifacts/coverage-$T 70
locked capture-protocol ./scripts/pdd.sh capture --job protocol --output artifacts/pdd-$T/protocol
locked capture-tests ./scripts/pdd.sh capture --job tests --output artifacts/pdd-$T/tests
locked capture-controlled ./scripts/pdd.sh capture --job controlled --output artifacts/pdd-$T/controlled
locked capture-stryker ./scripts/pdd.sh capture --job stryker --output artifacts/pdd-$T/stryker
plain aggregate ./scripts/pdd.sh aggregate --output artifacts/pdd-$T
echo "== final dirty=$(git status --porcelain | wc -l)"
echo CHECKS-DONE
