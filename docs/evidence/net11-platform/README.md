# Evidence for P1: the net11.0 target

Local four-job capture (`./scripts/pdd.sh capture` for protocol, tests, controlled and
stryker, then `aggregate`) on commit `2939674`, clean worktree, Linux x64 dev container,
SDK 11.0.100, runtime 11.0.0, on 2026-11-14. Machine evidence gate: passed.

| Job | Seconds |
| --- | --- |
| protocol | 188 |
| tests | 189 |
| controlled | 79 |
| stryker | 360 |

- `summary.md` and `receipt.json` are the aggregate's output, copied unchanged. The raw TRX,
  logs and Stryker report they link to stayed in `artifacts/pdd-p1/` and are not kept.
- `test-all-tfms-release.log` is `./test-all-tfms.sh Release` on commit `df225cc` (the same
  production source): 328 tests passed on each of the six library builds.
- Stryker against the `net11.0` build: 42 killed, 27 survived, 7 no coverage, 0 timeouts,
  3 compile errors, 26 ignored. Every outcome that needs a disposition matched one of the 54
  recorded ones, and all 16 required kills were killed. The source is unchanged, so the
  mutant keys are those the dispositions were recorded with.
