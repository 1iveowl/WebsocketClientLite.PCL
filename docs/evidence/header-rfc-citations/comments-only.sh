#!/usr/bin/env bash
# Shows that the Protocol.dfy change is comment lines only. Run from the repository root.
set -euo pipefail
base="${1:-28e30cb}"
strip() { sed -E 's#^\s*//.*$##; s#\s*//.*$##' | grep -v -E '^\s*$'; }
echo "base: $(git rev-parse "$base")"
echo "removed lines: $(git diff -U0 "$base" -- verification/Protocol.dfy | grep -c -E '^-[^-]' || true)"
echo "added lines that are not // comments: $(git diff -U0 "$base" -- verification/Protocol.dfy | grep -E '^\+[^+]' | grep -c -v -E '^\+\s*//' || true)"
echo "added comment lines: $(git diff -U0 "$base" -- verification/Protocol.dfy | grep -c -E '^\+\s*//' || true)"
echo "code lines with a trailing // comment (stripping assumes none): $(grep -c -E '^\s*[^/[:space:]].*//' verification/Protocol.dfy || true)"
if diff <(git show "$base:verification/Protocol.dfy" | strip) <(strip < verification/Protocol.dfy); then
  echo "comment-stripped files: identical"
else
  echo "comment-stripped files: DIFFERENT"; exit 1
fi
