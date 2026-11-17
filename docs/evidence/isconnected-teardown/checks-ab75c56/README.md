# Checks after the reconnect count change, made on 2026-11-17

Same host as the [parent folder](../README.md). Every check ran once, at `ab75c56` (the count change
on `chore/reconnect-case-count`) with a clean tree. None was repeated.

`tools/Pdd/ReconnectDefects.cs` now names `EndedConnectionReportsNotConnected` (2 cases) in
`ReconnectCaseCounts`. The baseline total is the sum of `ReconnectCaseCounts` plus the runtime probe,
as `RetentionCaseCounts` does in `RetentionDefects.cs`, instead of the literal 10 on line 77.

| Check | Result |
| --- | --- |
| `./scripts/pdd.sh reconnect-defect`, [reconnect-defect/](reconnect-defect/results.json) | run and passed, 21 s: 12 baseline cases, `missing-removal` detected, `dirty_worktree: false`. At `9fc5e6c` the same check failed on the count ([../checks/](../checks/reconnect-defect/results.json)) |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| Pdd tests, `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release` | run and passed: 318 of 318 |
| `capture --job protocol` | run and passed, 186 s |
| `capture --job tests` | run and passed, 192 s: six library builds, all on .NET 11.0.0 hosts |
| `capture --job controlled` | run and passed, 81 s |
| `capture --job stryker` | run and passed, 285 s |
| `aggregate`, [summary.md](summary.md) | run and passed: machine evidence gate passed, `EPOCH-001-TEST` passed; review evidence not-run |

The exit lines are in [capture.exit](capture.exit). The capture directories and `receipt.json`
(900 KB) are not committed; `summary.md` is the aggregate's own summary of them. Paths under the
scratch directory are written `<scratch>`.
