# IsConnected after a connection ends: evidence made on 2026-11-17

Everything here was made on 2026-11-17 on one host: a Linux x86-64 Ubuntu 24.04.5 dev container on
a Debian host kernel 7.2.12+deb13-amd64, .NET SDK 11.0.100, test host net11.0, Release. The test is
`ReconnectTests.EndedConnectionReportsNotConnected`, with two rows: `server-close` (the server
sends Close 1000) and `unsubscribe` (the subscriber disposes its subscription).

## Claim and expected answer

- Claim: row 20 of the transition table in
  [verification/reconnect-decision.md](../../../verification/reconnect-decision.md):
  "Terminate(g) | Og | Og | NotifyNotConnected | 303 | I6; teardown reports not-connected". The same
  file, "Seeded defects, both ways", says that nothing in the 269 tests it counted then checks that
  `IsConnected` becomes false when a connection ends. I6 is in
  [verification/reconnect-intent.md](../../../verification/reconnect-intent.md): "A per-subscription
  identity gates only the final false notification."
- Expected answer: written by hand from that row. `IsConnectedObservable` gives `false` (the
  initial value), `true` (the handshake installed the sender), then `false` (teardown), and nothing
  else. Not an RFC question: RFC 6455 does not specify the client's API.
- The check: `src/main/WebsocketClientLite/ClientWebSocketRx.cs`, line 303, in the teardown
  `Finally`: `if (ReferenceEquals(_generation, generation)) SetIsConnected(false);`.

## Ordering

No sleep orders anything. The test waits for `true` from its own `IsConnectedObservable` observer,
then ends the connection: it releases the server's Close with a `TaskCompletionSource`, or disposes
the subscription itself. It wraps the client's observable in its own `Finally` and waits for that
action. Rx's `Finally` disposes its source before it runs its action, and the client's teardown
`Finally` is part of that source, so the test's action runs after line 303 on the same thread. The
assertion then reads the recorded values. A missing notification therefore fails the assertion; it
does not become a timeout. The 15-second limits are diagnostic bounds on the handshake and on the
end of the connection.

## Runs

Each run used its own `git archive <commit> | tar -x` copy in a scratch directory, never the working
checkout. [run.sh](run.sh) runs:

```text
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release -p:LibTfm=net11.0
  --filter "FullyQualifiedName~ReconnectTests.EndedConnectionReportsNotConnected"
  --results-directory <out> --logger "trx;LogFileName=<run>.trx" --logger "console;verbosity=normal"
```

It writes `<run>.log`, `<run>.trx`, `<run>.exit` (exit code and duration) and `<run>.identity.txt`
(commit, edit and SHA-256 of `ClientWebSocketRx.cs` and `ReconnectTests.cs` as built). `<run>.diff` is
`diff -ruN -x bin -x obj` against an untouched copy of the same commit. No run was repeated.

| Run | Commit | Edit | Result |
| --- | --- | --- | --- |
| [failing-first/remove-notify](failing-first/) | `9fc5e6c` | Line 303 of `ClientWebSocketRx.cs` removed | run and failed, 0 of 2: `IsConnected did not become false when the connection ended (server-close): False, True`, and the same for `unsubscribe` |
| [current/current](current/) | `9fc5e6c` | None | run and passed, 2 of 2 |
| [variant-echo-server/](variant-echo-server/) | `a52cfff` | As above, for each of its two runs | `remove-notify`: run and failed, 0 of 2, the same failure lines. `current`: run and passed, 2 of 2 |

The `unsubscribe` row takes about 3 seconds in every run. A synchronous unsubscribe stops the reader
and then waits up to 3 seconds for the Close handshake (`WebsocketConnectionHandler.cs`, lines 337 to
363: "Prefer DisposeAsync for a fully awaited close"). The test's server does not answer it.

`variant-echo-server/` holds a variant in which the `unsubscribe` row's server mirrors Close with
`EchoLoopAsync` ([variant-test.diff](variant-echo-server/variant-test.diff)), made to remove that
wait. The wait stayed, because the reader has already stopped and never reads the reply. The
variant commit `a52cfff` was not kept on the branch. Its runs are kept here.

## Checks on the branch

| Check | Result |
| --- | --- |
| `./test-all-tfms.sh Release` at `9fc5e6c`, [checks/test-all-tfms.log](checks/test-all-tfms.log) | run and passed: 330 of 330 on each of netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 and net11.0, 160 s |
| `./scripts/pdd.sh reconnect-defect` at `9fc5e6c`, [checks/reconnect-defect/](checks/reconnect-defect/results.json) | **run and failed**: `Incomplete reconnect baseline cases`. All 12 baseline cases passed, and `missing-removal` was detected. `tools/Pdd/ReconnectDefects.cs`, line 77, requires exactly 10 baseline results; the new theory adds 2. The worktree held this folder's untracked files (`dirty_worktree: true`); the sources were those of `9fc5e6c` |
| `./scripts/pdd.sh validate` | run and passed: 16 requirements |
| Pdd tests, `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release` | run and passed: 318 of 318 |
| `capture` jobs and `aggregate` | not run. `capture --job controlled` runs `reconnect-defect`, so it would fail on the same count |

## What this does not cover

- The failing run used the net11.0 library build only. The six-target matrix ran the unchanged test.
- An error ending the connection (a read failure, or a failed handshake after `true`) is not a row.
  Both reach the same `Finally`.
- Concurrent subscriptions (U1 in `reconnect-decision.md`) and teardown after the client is
  disposed (row 23, U3) are not tested.
- The ordering relies on Rx's `Finally` disposing its source before it runs its action. That is
  part of the trusted Rx terminal semantics named in `docs/pdd-phase-3-plan.md`.
- The edit is a hand-written C# change. It shows that the test detects the missing notification,
  and says nothing about the other surviving defects named in `reconnect-decision.md`
  (`subscribe-keeps-generation`, `terminate-passes-no-identity`); they were not run.

Paths under the scratch directory are written `<scratch>` in the logs, TRX and JSON files.
