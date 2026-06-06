#!/usr/bin/env bash
#
# Parses a cobertura coverage report, prints a summary (and writes it to the
# GitHub Actions job summary when available), and fails if line coverage is
# below a minimum threshold.
#
# Usage: ./scripts/check-coverage.sh [results-dir] [min-line-percent]
set -euo pipefail

RESULTS_DIR="${1:-./coverage}"
MIN_LINE="${2:-70}"

report=$(find "${RESULTS_DIR}" -name "coverage.cobertura.xml" | head -1)
if [ -z "${report}" ]; then
  echo "No coverage.cobertura.xml found under ${RESULTS_DIR}" >&2
  exit 1
fi

line_rate=$(grep -m1 -oE 'line-rate="[0-9.]+"' "${report}" | grep -oE '[0-9.]+' | head -1)
branch_rate=$(grep -m1 -oE 'branch-rate="[0-9.]+"' "${report}" | grep -oE '[0-9.]+' | head -1)
line_pct=$(awk "BEGIN { printf \"%.1f\", ${line_rate} * 100 }")
branch_pct=$(awk "BEGIN { printf \"%.1f\", ${branch_rate} * 100 }")

echo "Line coverage:   ${line_pct}%"
echo "Branch coverage: ${branch_pct}%"
echo "Minimum line:    ${MIN_LINE}%"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Code coverage"
    echo ""
    echo "| Metric | Coverage |"
    echo "|--------|---------:|"
    echo "| Line   | ${line_pct}% |"
    echo "| Branch | ${branch_pct}% |"
    echo ""
    echo "_Minimum line-coverage gate: ${MIN_LINE}%_"
  } >> "${GITHUB_STEP_SUMMARY}"
fi

below=$(awk "BEGIN { print (${line_pct} < ${MIN_LINE}) ? 1 : 0 }")
if [ "${below}" -eq 1 ]; then
  echo "::error::Line coverage ${line_pct}% is below the required ${MIN_LINE}%."
  exit 1
fi

echo "Line coverage ${line_pct}% meets the ${MIN_LINE}% minimum."
