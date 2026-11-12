# Region counts, one script for both commits (2026-11-11)

Made on 2026-11-11 after the M3 review of PR #142 (finding 10). S5's
`../baseline/count_region.py` and S6's `../after/count_region_after.py` counted the
stateful region of `ClientWebSocketRx.cs` with two copies of the same rules. They are
kept unchanged, because they made the recorded counts in
`../baseline/region-counts.txt` and `../after/region-counts.txt`.

`count_region.py` holds the rules once. A spec gives only what differs between the two
commits: the anchor lines, the line ranges of each part, the lifecycle-state patterns
and the new files. `before.json` (`fa96d59`) and `after.json` (`bd743b3`) take their
anchors, ranges and patterns verbatim from the two recorded scripts. Any later count,
before or after, uses this script, so that a change to a rule changes both sides.

The two copies' rules were the same: the after copy's extra `///` exclusion for the
new files is already covered by the "starts with `//`" rule, so it changed no count.

Commands, run on `git archive` copies of the two commits outside the repository:

```bash
git archive fa96d59 | tar -x -C <scratch>/fa96d59
git archive bd743b3 | tar -x -C <scratch>/bd743b3
python3 -I count_region.py before.json <scratch>/fa96d59 > before-counts.txt
python3 -I count_region.py after.json <scratch>/bd743b3 > after-counts.txt
```

Result: both reproduce the recorded counts exactly, with the same file hashes.

| Commit | File SHA-256 | Physical / code / lifecycle-state lines | Lock sites | Recorded |
| --- | --- | --- | --- | --- |
| `fa96d59` | `ccc2a82e…d7f10` | 97 / 88 / 15 | 6 | 97 / 88 / 15, 6 |
| `bd743b3` | `ba241d38…459f8` | 109 / 95 / 12 (S5 pattern), 16 (after pattern) | 6 | 109 / 95 / 12 and 16, 6 |
| `bd743b3`, new files | | `ReconnectDecision.cs` 92 / 38, `ReconnectDecisionTests.cs` 170 / 138 | | the same |
