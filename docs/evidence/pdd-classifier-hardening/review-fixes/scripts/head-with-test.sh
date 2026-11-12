#!/usr/bin/env bash
# usage: head-with-test.sh <commit> <test-file> <filter>
# M6 fix of #145, 2026-11-12. Copies <commit> with git archive into a new directory, puts
# <test-file> in as tools/Pdd.Tests/ClassifierHardeningTests.cs, makes the copy its own git
# repository and runs the filtered Pdd tests there. The working checkout is not changed.
set -eu
repo=/home/vscode/worktrees/s10
copy=$(mktemp -d /tmp/m6-head.XXXXXX)
git -C "$repo" archive "$1" | tar -xf - -C "$copy"
echo "source: git archive $(git -C "$repo" rev-parse "$1"), copy: $copy"
cp "$2" "$copy/tools/Pdd.Tests/ClassifierHardeningTests.cs"
echo "tools/Pdd.Tests/ClassifierHardeningTests.cs replaced with $2 (sha256 $(sha256sum "$2" | cut -c1-16))"
cd "$copy" && git init -q && git add -A && git -c user.name=copy -c user.email=copy@invalid commit -qm copy
set +e
dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release --filter "$3"
