#!/usr/bin/env bash
#
# Runs the unit-test suite against every target-framework build of the library
# (netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0).
#
# The test project itself targets a single runnable TFM, but -p:LibTfm forces it
# to link a specific library build. A netstandard2.0/2.1 assembly runs fine on a
# modern .NET runtime, so this exercises each build's #if-conditional code paths
# even on a machine that only has the latest .NET runtime installed.
#
# Usage: ./test-all-tfms.sh [Debug|Release]
set -uo pipefail

TFMS=(netstandard2.0 netstandard2.1 net8.0 net9.0 net10.0)
PROJECT="src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj"
CONFIG="${1:-Debug}"

fail=0
for tfm in "${TFMS[@]}"; do
  echo "=================================================================="
  echo "  Testing against library build: ${tfm} (config: ${CONFIG})"
  echo "=================================================================="
  if ! dotnet test "${PROJECT}" -c "${CONFIG}" -p:LibTfm="${tfm}" --nologo; then
    echo "FAILED: ${tfm}"
    fail=1
  fi
done

echo "=================================================================="
if [ "${fail}" -ne 0 ]; then
  echo "  RESULT: one or more target frameworks FAILED."
  exit 1
fi
echo "  RESULT: all target frameworks passed."
