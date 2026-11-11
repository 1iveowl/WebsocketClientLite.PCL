#!/usr/bin/env bash
cd /workspaces/WebsocketClientLite.PCL
L=artifacts/s4-checks
step() { name=$1; shift; s=$(date +%s); echo "== $name start $(date -u +%FT%TZ) commit $(git rev-parse HEAD) dirty=$(git status --porcelain | wc -l)"; "$@" > $L/$name.log 2>&1; c=$?; echo "exit=$c seconds=$(( $(date +%s) - s ))" > $L/$name.exit; echo "== $name exit=$c seconds=$(( $(date +%s) - s ))"; }
step pdd-validate ./scripts/pdd.sh validate
step pdd-tests dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release
step test-all-tfms ./test-all-tfms.sh Release
step coverage-tests dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/protocol-coverage-s4
step coverage-gate ./scripts/check-coverage.sh artifacts/protocol-coverage-s4 70
step capture-protocol ./scripts/pdd.sh capture --job protocol --output artifacts/pdd-s4/protocol
step capture-tests ./scripts/pdd.sh capture --job tests --output artifacts/pdd-s4/tests
step capture-controlled ./scripts/pdd.sh capture --job controlled --output artifacts/pdd-s4/controlled
step capture-stryker ./scripts/pdd.sh capture --job stryker --output artifacts/pdd-s4/stryker
step aggregate ./scripts/pdd.sh aggregate --output artifacts/pdd-s4
echo "== final dirty=$(git status --porcelain | wc -l)"
echo CHECKS-DONE
