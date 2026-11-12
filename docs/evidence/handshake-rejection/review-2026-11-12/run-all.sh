#!/usr/bin/env bash
# Review fix on #144 (2026-11-12): failing-first runs of the final test, 46ca940.
# Builds both copies, then runs each batch, each step in one hold of the shared timing
# lock, and prints when each lock was taken. Uses S11's tools with this folder as EVIDENCE.
set -u
REPO=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
T=$REPO/docs/evidence/handshake-rejection/tools
export SCRATCH=/tmp/s11-review-scratch EVIDENCE=$REPO/docs/evidence/handshake-rejection/review-2026-11-12
step() { echo "== $1 waiting for the lock $(date -u +%FT%TZ)"; shift
  flock ~/worktrees/.timing.lock bash -c 'echo "== lock taken $(date -u +%FT%TZ)"; "$@"; c=$?; echo "== exit=$c $(date -u +%FT%TZ)"' _ "$@"; }
step prepare-mutant "$T/prepare.sh" review-mutant 46ca940 throw-removed
step prepare-unchanged "$T/prepare.sh" review-unchanged 46ca940 none
step review-mutant-plain "$T/batch.sh" review-mutant-plain review-mutant 15 plain covering+new
step review-mutant-loaded "$T/batch.sh" review-mutant-loaded review-mutant 15 loaded covering+new
step review-unchanged-loaded "$T/batch.sh" review-unchanged-loaded review-unchanged 15 loaded covering+new
echo "== RUN-ALL-DONE $(date -u +%FT%TZ)"
