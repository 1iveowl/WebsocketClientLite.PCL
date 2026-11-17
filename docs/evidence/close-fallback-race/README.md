# Fallback Close test race: evidence made on 2026-11-17

Everything except the CI run was made on 2026-11-17 on one host: an Ubuntu 24.04.5 dev container on
a Debian host kernel 7.2.12+deb13-amd64, x86-64, .NET SDK 11.0.100, test host net11.0, Release. The
test is `CloseBoundaryTests.ParserCompletionAwaitsFallbackCloseBeforeObserverCompletion`, one case.
It backs `CLOSE-004-CONTROLLED` and `CLOSE-005-CONTROLLED` in `verification/requirements.json`, and
it is the `completion-fallback` case of `tools/Pdd/BoundaryDefects.cs`.

## The failure

CI run 44123755687 (pull request #179, `releases/10.6.0` at `63441fd`), attempt 1, job
`build-and-test`: the test **failed** on the netstandard2.0 library build, with "Parser completion
did not start fallback Close" at `CloseBoundaryTests.cs`, line 112
([ci/run-44123755687-attempt-1-netstandard2.0.trx](ci/run-44123755687-attempt-1-netstandard2.0.trx)).
The other five targets passed 330 of 330. Attempt 2 reran the failed job, and it passed.

## Cause

The assertion read `Lazy<Task>.IsValueCreated` of the connection's `_closeHandshakeOnce` after the
Close write had been entered. The factory is `() => Task.Run(() => DisconnectWebsocket(sender))`
(`WebsocketConnectionHandler.cs`, line 138). `Task.Run` queues the Close, and a pool thread can
reach the write, and so `WriteEntered`, before the factory returns and `Lazy` publishes the value.
The test's continuation can then read `IsValueCreated` as false although the fallback Close has
started. This is a race in the test's check, not in the library: the Close frame was written.

## Runs

Each run used its own `git archive <commit> | tar -x` copy in a scratch directory. [run.sh](run.sh)
runs the test with
`--filter "FullyQualifiedName~CloseBoundaryTests.ParserCompletionAwaitsFallbackCloseBeforeObserverCompletion"`,
`-p:LibTfm=netstandard2.0` (the target that failed in CI) and a TRX logger, and writes
`<run>.log`, `<run>.trx`, `<run>.exit` and `<run>.identity.txt`. `<run>.diff` is
`diff -ruN -x bin -x obj` against an untouched copy of the same commit. No run was repeated.

The `widen` edit makes the factory sleep 500 ms after `Task.Run` and before it returns, in a scratch
copy only. It holds open the gap the CI run hit; it forces that one order and shows no other.

| Run | Commit | Edit | Result |
| --- | --- | --- | --- |
| [reproduction/widen-publication](reproduction/) | `63441fd` | `widen` | run and failed: "Parser completion did not start fallback Close" |
| [reproduction/current](reproduction/) | `63441fd` | None | run and passed, 1 of 1 |
| [fixed/fixed-widened](fixed/) | `fc91f55` | `widen` | run and passed, 1 of 1 |
| [fixed/fixed-current](fixed/) | `fc91f55` | None | run and passed, 1 of 1 |
| [fixed/fixed-completion-fallback](fixed/) | `fc91f55` | The finally's `await closeOnceAsync().ConfigureAwait(false);` removed | run and failed: "Parser completion did not start fallback Close" |

## The fix

`fc91f55` asserts that the Close write was entered and wrote opcode 8
(`h.WriteEntered.Task.IsCompleted && h.Frames.Contains(8)`) instead of `IsValueCreated`. The
message is unchanged, so `BoundaryDefects.cs` still matches it. No library code changes.

## Checks at `fc91f55`

| Check | Result |
| --- | --- |
| `./test-all-tfms.sh Release`, [checks/test-all-tfms.log](checks/test-all-tfms.log) | run and passed: 330 of 330 on each of netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 and net11.0, 161 s |
| `./scripts/pdd.sh boundary-defects`, [checks/boundary-defects/](checks/boundary-defects/results.json) | run and passed: baseline passed and all 13 isolated defect runs detected; `completion-fallback-0` failed with "Parser completion did not start fallback Close". Only the baseline and `completion-fallback-0` folders are kept |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| Pdd tests, `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release` | run and passed: 318 of 318 |

## What this does not cover

- The CI failure happened once, on a GitHub runner. The local reproduction forces the gap with a
  sleep in a scratch copy; it does not show how often the gap occurs without it.
- The failing and fixed runs used the netstandard2.0 library build only.
- `IsValueCreated` is still read in `CancelledParserLeavesCloseToDisposalAndDoesNotCompleteObserver`
  (`Assert.False`), where no Close has started, so the gap cannot make it fail. It is not changed.

Paths under the scratch directory are written `<scratch>` in the logs, TRX and JSON files.
