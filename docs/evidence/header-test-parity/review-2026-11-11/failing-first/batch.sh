#!/usr/bin/env bash
cd /home/vscode/worktrees/fix-140
R=docs/evidence/header-test-parity/tools/run-review.sh
echo "== failing-first batch start $(date -u +%FT%TZ)"
$R current cdcc233 none
$R reads-past-header cdcc233 reads-past-header
$R rsv2-rsv3 cdcc233 rsv2-rsv3
$R unknown-opcodes-sequence cdcc233 unknown-opcodes-sequence
$R known-removed cdcc233 known-removed
$R known-sequence-removed cdcc233 known-sequence-removed
$R limit-plus-one cdcc233 limit-plus-one
$R eof-as-other cdcc233 eof-as-other
$R eof-as-other-c82da89 c82da89 eof-as-other
echo "== failing-first batch end $(date -u +%FT%TZ)"
