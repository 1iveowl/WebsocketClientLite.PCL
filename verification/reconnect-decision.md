# Reconnect lifecycle decision (Experiment 1)

Written 2026-11-11 in step S5 of `docs/pdd-10.3-session-plan.md`, from base commit
`fa96d59` (`origin/releases/10.3.0.0`). This step changes no production or test
code. It writes down the decision that `ClientWebSocketRx` makes today under
`_lifecycleGate` and records the numbers before any change. Step S6 implements the
table as a pure function, measures again and adds the results and the
recommendation below.

The intended behaviour is `verification/reconnect-intent.md` (P3-D). Where the
intent is silent, the table keeps today's behaviour and lists the case under
[Undecided cases](#undecided-cases). The table describes the code at `fa96d59`; it
proposes no change.

## References verified

Checked on 2026-11-11 at `fa96d59`. Kept evidence:
`docs/evidence/reconnect-decision/baseline/` (`identity.json` has the file hashes).

| Plan or brief says | Found | Difference |
| --- | --- | --- |
| `_lifecycleGate`, `_disposed`, `_generation` at `ClientWebSocketRx.cs` lines 24 to 26 | Lines 24, 25, 26 | None |
| Lock sites at lines 251, 263, 279 and 301 | `lock (_lifecycleGate)` at 251, 263, 279, 301 | None |
| `_disposed = true` under the gate at lines 331 and 368 | 331 in `Dispose(bool)`, 368 in `DisposeAsync` | None. There is no other `lock` in the file |
| The brief: "around lines 251 to 303, and in Dispose and DisposeAsync" | The final notification is at 303, inside the lock at 301 | None |
| Intent in `verification/reconnect-intent.md` and `docs/p3-d-validation.md` | Both present; the intent defers concurrent active subscriptions (line 17) | None |
| Seeded defects in `tools/Pdd/ReconnectDefects.cs` | One case, `missing-removal`, test `TerminalCallbackSuccessorSurvivesOldCleanup`, assertion "Old service retained registration" | The one seeded defect edits cleanup (line 290), not a decision line. See [Seeded defects](#seeded-defects) |
| "No-adoption" rule at `docs/pdd-phase-3-plan.md` lines 519 and 520 | "A justified" ends line 519, "no-adoption decision is a successful deliverable." is line 520 | None |
| "Small pure decision" rule in the same file's tooling table, line 238 | Line 238, row "More Dafny lifecycle modeling" | None |

Also checked, because the table depends on them:

- Stryker does not mutate this file. `src/main/WebsocketClientLiteTest/stryker-config.json`
  mutates only `**/Service/WebsocketConnectionHandler.cs`.
- `EPOCH-001` in `verification/requirements.json` is the requirement for this
  region. Its production path is `ClientWebSocketRx.DisposeAsync` and its test
  obligation names the five `ReconnectTests` methods.
- The handshake calls `InitSender` through `setSenderAction` at
  `Service/WebsocketConnectionHandler.cs` line 106. An exception thrown there
  leaves `ConnectWebsocket` and reaches the subscriber as `OnError`.

## The decision

### State

The decision reads and writes two fields: `_disposed` (bool) and `_generation`
(the identity of the most recently admitted subscription, or none). Each
subscription creates its own identity `g` at line 250. An event from subscription
`g` only needs to know how `_generation` relates to `g`, so the table uses six
state classes:

| State | `_disposed` | `_generation` |
| --- | --- | --- |
| O0 | false | none: no subscription was ever admitted |
| Og | false | `g`, the subscription the event comes from |
| Ox | false | another subscription's identity |
| D0 | true | none |
| Dg | true | `g` |
| Dx | true | another subscription's identity |

`_generation` is never reset to none, so O0 and D0 are only reached before the
first admission. A terminating subscription does not clear it.

### Events

| Event | Where | When |
| --- | --- | --- |
| Subscribe(g) | Lock L1, lines 250 to 259 | Synchronously in `Subscribe`, with a new `g` |
| Register(g) | Lock L3, lines 279 to 285 | Synchronously in the same `Subscribe` call, after the `Initialized` status (line 274) is sent to the subscriber; the `Observable.Using` resource factory |
| Connect(g) | Lock L2, lines 263 to 269 (`InitSender`) | Asynchronously, after the handshake succeeds (`WebsocketConnectionHandler.cs` line 106) |
| Terminate(g) | Lock L4, lines 299 to 305 | The Rx `Finally` of `g`'s status observable: once, after `OnError`, `OnCompleted` or unsubscription. A subscription rejected at Subscribe never reaches it, because line 256 returns before the `Finally` is built |
| Dispose | Lock L5, line 331 (`Dispose(bool)`); lock L6, line 368 (`DisposeAsync`, which also reaches L5 through `Dispose(true)` at line 377) | `Dispose()` or `DisposeAsync()`, any number of times |

One subscription produces, in order: Subscribe(g), then optionally Register(g),
then optionally Connect(g), then Terminate(g). Terminate(g) can follow any of the
earlier events. Dispose and the events of other subscriptions can fall between any
two of them.

### Effects

The brief's effects are admit, reject with `ObjectDisposedException`, notify
not-connected and do nothing. Admission happens at three points, so the table
names each one:

| Effect | Brief's effect | What the code does |
| --- | --- | --- |
| Admit | admit | Stores `g` as `_generation` and continues to build the subscription |
| Register | admit | Creates the service and adds it to `_activeConnections` under the lock |
| Install | admit | Sets `Sender` and `NegotiatedSubprotocols` and calls `SetIsConnected(true)`, all under the lock |
| Reject | reject with `ObjectDisposedException` | At L1: `OnError` and `Disposable.Empty`, no service. At L3 and L2: `throw`, which reaches the subscriber as `OnError` and is followed by Terminate(g) |
| NotifyNotConnected | notify not-connected | `SetIsConnected(false)` under the lock. `SetIsConnected` swallows `ObjectDisposedException` when the subject is already disposed (lines 146 to 156) |
| CloseAdmission | (the dispose event's state change) | Sets `_disposed`. The caller then drains `_activeConnections` and disposes the subject outside the lock; that is not part of the decision |
| Nothing | do nothing | No field and no observable changes |

### Transition table

Every state and event pair has a row: 6 states × 5 events = 30 rows. A row is
"unreachable" when no sequence of events from the code can produce it; the row
still gives what the code at `fa96d59` would do, so that a total function in S6
can match it. "Basis" names the intent sentence, test or undecided case that fixes
the row.

Intent sentences, quoted from `verification/reconnect-intent.md`:

- **I1** "Disposal is permanent: a subscription after disposal must terminate with ObjectDisposedException without admitting a service."
- **I2** "Setup already in progress must either be registered before disposal takes ownership or rejected and cleaned up."
- **I3** "A successor subscribed from an old terminal callback is sequential logical reconnect even if old cleanup has not returned."
- **I4** "Concurrent active subscriptions' aggregate IsConnected/Sender policy is explicitly deferred."
- **I5** "Sender is the most recent successful handshake, not a reconnecting proxy, and may remain non-null when disconnected."
- **I6** "A per-subscription identity gates only the final false notification."

| # | Event | State | Next | Effect | Line | Basis |
| ---: | --- | --- | --- | --- | --- | --- |
| 1 | Subscribe(g) | O0 | Og | Admit | 251, 258 | First subscription |
| 2 | Subscribe(g) | Og | Og | Admit | 258 | Unreachable: `g` is new at line 250, so `_generation` cannot already be `g` |
| 3 | Subscribe(g) | Ox | Og | Admit | 258 | Sequential reconnect (I3). When `x` is still active: U1 |
| 4 | Subscribe(g) | D0 | D0 | Reject | 253 to 256 | I1; P3-D counterexample 1 |
| 5 | Subscribe(g) | Dg | Dg | Reject | 253 to 256 | Unreachable, as row 2 |
| 6 | Subscribe(g) | Dx | Dx | Reject | 253 to 256 | I1 |
| 7 | Register(g) | O0 | O0 | Register | 281 to 284 | Unreachable: Register(g) follows an admitted Subscribe(g) |
| 8 | Register(g) | Og | Og | Register | 281 to 284 | I2, registered before disposal |
| 9 | Register(g) | Ox | Ox | Register | 281 to 284 | U1: only reachable when another subscription is admitted between Subscribe(g) and Register(g), for example from the subscriber's `Initialized` callback, so both are active |
| 10 | Register(g) | D0 | D0 | Reject | 281 | Unreachable, as row 7 |
| 11 | Register(g) | Dg | Dg | Reject | 281 | I2, rejected; disposal fell between Subscribe(g) and Register(g) |
| 12 | Register(g) | Dx | Dx | Reject | 281 | I1, I2; with U1 |
| 13 | Connect(g) | O0 | O0 | Install | 265 to 268 | Unreachable, as row 7 |
| 14 | Connect(g) | Og | Og | Install | 265 to 268 | I5 |
| 15 | Connect(g) | Ox | Ox | Install | 265 to 268 | U1 and U2. Today the generation is not checked: `g`'s sender replaces the current one and `IsConnected` becomes true |
| 16 | Connect(g) | D0 | D0 | Reject | 265 | Unreachable, as row 7 |
| 17 | Connect(g) | Dg | Dg | Reject | 265 | I1: disposal is permanent. `SetupAdmissionIsOwnedUntilTeardown` asserts `Sender` is null after disposal during setup |
| 18 | Connect(g) | Dx | Dx | Reject | 265 | I1 |
| 19 | Terminate(g) | O0 | O0 | Nothing | 303 | Unreachable, as row 7. `ReferenceEquals(null, g)` is false |
| 20 | Terminate(g) | Og | Og | NotifyNotConnected | 303 | I6; teardown reports not-connected |
| 21 | Terminate(g) | Ox | Ox | Nothing | 303 | I3, I6; P3-D counterexample 2. When `x` is concurrent rather than a successor: U1 |
| 22 | Terminate(g) | D0 | D0 | Nothing | 303 | Unreachable, as row 7 |
| 23 | Terminate(g) | Dg | Dg | NotifyNotConnected | 303 | U3 |
| 24 | Terminate(g) | Dx | Dx | Nothing | 303 | I6; with U1 |
| 25 | Dispose | O0 | D0 | CloseAdmission | 331, 368 | I1 |
| 26 | Dispose | Og | Dg | CloseAdmission | 331, 368 | I1, I2 |
| 27 | Dispose | Ox | Dx | CloseAdmission | 331, 368 | I1, I2. Dispose has no generation, so rows 26 and 27 are the same row |
| 28 | Dispose | D0 | D0 | Nothing | 331, 368 | I1: idempotent. `DisposeAsync` always reaches this row once, through `Dispose(true)` |
| 29 | Dispose | Dg | Dg | Nothing | 331, 368 | As row 28 |
| 30 | Dispose | Dx | Dx | Nothing | 331, 368 | As row 28 |

Rows 28 to 30: the code writes `_disposed = true` again. The next state equals
the current one, so the table records Nothing.

Summary of today's rule:

- Subscribe overwrites `_generation` unless disposed. Register and Connect look
  only at `_disposed`. Terminate looks only at `_generation`. Dispose only sets
  `_disposed`.
- Of the 30 rows, 8 are unreachable: 2, 5, 7, 10, 13, 16, 19 and 22. Dispose
  carries no generation, so rows 26 and 27 are one transition, and rows 29 and 30
  are one. That leaves 20 distinct reachable transitions.

### Undecided cases

The intent does not decide these. The table keeps today's behaviour for each.

- **U1. Concurrent active subscriptions** (rows 3, 9, 12, 15, 21 and 24 when the
  other subscription is still active). The intent defers the aggregate
  `IsConnected` and `Sender` policy (I4). Today: the most recent Subscribe owns the
  final not-connected notification, and the most recent Connect owns `Sender`.
  Consequences that no test defines: when the newer subscription ends while the
  older one is still connected, `IsConnected` becomes false; when the older one
  ends first, nothing is reported; when the older handshake completes after the
  newer one, the older sender replaces the newer one.
- **U2. Connect after the subscription's own Terminate** (row 14 when no
  successor was admitted, row 15 when one was). If a handshake completes after its
  subscription was cancelled and the client is not disposed, today's code installs
  the sender and reports connected. S5 did not establish whether this is
  reachable: cancellation disposes the service through the `Using` disposer, which
  normally fails the handshake first. No test covers it. The intent says only that
  cancellation must clean up a service whose creation completes late.
- **U3. Final notification after disposal** (row 23). The intent says nothing about
  `IsConnected` after disposal. Today the notification is attempted and, if the
  subject is already disposed, `SetIsConnected` swallows the exception.
  `IntegrationTests.DisposingClientBeforeSubscription_DoesNotThrowFromTeardown`
  pins only that this does not throw.
- **U4. Notifications run under the lock.** Install and NotifyNotConnected call the
  `IsConnectedObservable` subscribers while `_lifecycleGate` is held. A subscriber
  that subscribes or disposes on the same thread re-enters the lock (Monitor is
  reentrant); one that waits for another thread that needs the lock would block.
  The intent is silent. This is a property of the shell, not of the table, but S6
  must keep it: the effect has to run where it runs today.

### The P3-D counterexamples as event sequences

From `verification/reconnect-intent.md`, "Reduced counterexamples". The P3-D fix
is what makes the expected column hold.

1. **Admission after disposal.** Dispose, then Subscribe(g1).
   Expected: O0 → D0 (row 25), then Reject (row 4). At `6709c99` the subscription
   reached `WebsocketConnected`. Tested by `DisposedClientRejectsAdmission`, both
   disposal modes.
2. **Stale final notification.** Subscribe(g1), Register(g1), Connect(g1); `g1`
   completes and its terminal callback runs Subscribe(g2), Register(g2),
   Connect(g2); the callback returns; Terminate(g1).
   Expected: O0 → Og1 (row 1), rows 8 and 14, then from `g2`'s view row 3 (Og1 is
   Ox), rows 8 and 14 for `g2`, then Terminate(g1) in Ox (row 21): Nothing, so
   `IsConnected` stays true. At `6709c99` the last step set it to false. Tested by
   `TerminalCallbackSuccessorSurvivesOldCleanup`.

## Baseline (before numbers)

Measured 2026-11-11 in a detached worktree at `fa96d59` with an empty
`git status`. Commands, raw output and scripts:
`docs/evidence/reconnect-decision/baseline/` (`commands.json` lists each run,
including one failed attempt that measured nothing). These are local
measurements on one host; see `host.txt`.

### Reconnect cases: what each needs and what it establishes

The suite is `ReconnectTests`: 5 methods, 9 cases. It is the suite that
`EPOCH-001-TEST` names and `reconnect-defect` runs. Which table rows each case
reaches is by reading the test and the code; S5 did not instrument it.

| Case | Real loopback | Controlled schedule | Table rows reached | What it establishes beyond the rows |
| --- | --- | --- | --- | --- |
| `DisposedClientRejectsAdmission(false)` | Yes, but only so that a wrong admission could reach `WebsocketConnected` | No | 25, 4 | The rejection reaches the subscriber as `ObjectDisposedException` |
| `DisposedClientRejectsAdmission(true)` | As above | No | 25, 28, 4 | As above, after `DisposeAsync` |
| `TerminalCallbackSuccessorSurvivesOldCleanup` | Yes, two servers | Yes: the first server waits for `endFirst`; the old terminal callback is held until the successor handshakes; the old shared Close task is joined | 1, 8, 14; for `g2` 3, 8, 14; 21 for `g1`; at the end 20 for `g2` and 26 | Old registration removed, exactly one registration left, old `Sender` rejects sends, successor echo bytes. The seeded defect is detected here |
| `SetupAdmissionIsOwnedUntilTeardown("dispose")` | Yes | Yes: the handshake is held until `released` | 1, 8, 26, 23, then 29 from `using` | Registration drained, transport stream closed, `Sender` null |
| `SetupAdmissionIsOwnedUntilTeardown("async")` | Yes | Yes, as above | 1, 8, 26, 29, 23, then 29 | As above, through `DisposeAsync` |
| `SetupAdmissionIsOwnedUntilTeardown("unsubscribe")` | Yes | Yes, as above | 1, 8, 20, then 26 | Unsubscription during setup removes the registration and closes the stream |
| `SequentialRepeatOrRetryUsesFreshServices(false)` | Yes, two servers | Yes: the first server waits for `finishFirst` | 1, 8, 14, 20 for `g1`, then 3, 8, 14 for `g2` (or 3 before `g1`'s 20, which makes it 21; the order Rx uses for `Repeat` was not determined); at the end 20 and 26 | Fresh service per attempt, old registration removed, successor echo |
| `SequentialRepeatOrRetryUsesFreshServices(true)` | Yes, two servers | Yes, as above | As above, through `Retry` | As above |
| `FailedConnectTerminatesAndRemovesRegistration` | Yes, a 404 response | No | 1, 8, 20, then 26 | Registration removed after a failed handshake, `Sender` null. Lesson 10: its `Sender` assertion races against the line 95 handshake mutant |

Counts:

- Cases that need a real loopback: **9 of 9**.
- Cases that also need a controlled schedule: **6 of 9**
  (`TerminalCallbackSuccessorSurvivesOldCleanup` and the three
  `SetupAdmissionIsOwnedUntilTeardown` and two `SequentialRepeatOrRetryUsesFreshServices`
  cases).
- Cases whose decision is also covered by a plain test: **0 of 9**. No plain test
  of the decision exists before S6.
- Of the 20 distinct reachable transitions, 12 are reached by at least one case,
  by reading: rows 1, 3, 4, 8, 14, 20, 21, 23, 25, 26 (with 27), 28 and 29 (with
  30). Row 23 counts because disposal sets `_disposed` before it drains the
  service whose failure runs Terminate.
- 8 are reached by no `ReconnectTests` case, by reading: rows 6, 9, 11, 12, 15,
  17, 18 and 24. Row 6 makes the same decision as row 4. Rows 9 and 15 are
  undecided (U1, U2); rows 12 and 24 combine disposal with an older or concurrent
  subscription. Rows 11, 17 and 18 are decided by the intent but not
  reached: row 11 needs disposal between two locks in the same `Subscribe` call,
  and row 17 is what `SetupAdmissionIsOwnedUntilTeardown` guards against, but its
  held handshake fails once the transport is closed, so by reading the code never
  reaches line 265 with `_disposed` set. S5 did not measure line coverage.

Other tests pass through the region without being reconnect cases: every loopback
test in `IntegrationTests` and elsewhere reaches rows 1, 8, 14 and 20;
`IntegrationTests.DisposingClientBeforeSubscription_DoesNotThrowFromTeardown`
reaches row 23 with a disposed subject; `ErrorDeliveryStressTests` calls
`DisposeAsync` in each iteration. They are not counted above.

### Stateful lines and lock sites

`count_region.py` on `ClientWebSocketRx.cs` (SHA-256 `ccc2a82e…d7f10`). The region
is the fields (24 to 26), the `Observable.Create` body of
`WebsocketConnectWithStatusObservable` (248 to 307), `Dispose(bool)` (327 to 346)
and `DisposeAsync` (366 to 379). "Code" excludes blank and comment lines.
"Lifecycle-state lines" are code lines that mention `_lifecycleGate`,
`_disposed`, `_generation` or the local `generation`.

| Part | Physical lines | Code lines | Lifecycle-state lines | Lock sites |
| --- | ---: | ---: | ---: | --- |
| Fields | 3 | 3 | 3 | none |
| Connect observable | 60 | 57 | 10 | 251, 263, 279, 301 |
| `Dispose(bool)` | 20 | 15 | 1 | 331 |
| `DisposeAsync` | 14 | 13 | 1 | 368 |
| **Total** | **97** | **88** | **15** | **6** |

The decision itself is four conditions, `if (_disposed)` at 253, 265 and 281 and
`ReferenceEquals(_generation, generation)` at 303, and three assignments, at 258,
331 and 368.

### Reconnect suite run time

| Run | Wall seconds | Sum of case durations |
| --- | ---: | ---: |
| Build of the solution, Release | 4.56 | |
| `ReconnectTests`, netstandard2.0, with test project build | 10.80 | 9.212 |
| netstandard2.1, with build | 10.84 | 9.215 |
| net8.0, with build | 10.65 | 9.220 |
| net9.0, with build | 10.65 | 9.220 |
| net10.0, with build | 10.63 | 9.223 |
| net10.0, `--no-build`, three runs | 9.98, 9.98, 9.97 | 9.225, 9.225, 9.211 |
| `./scripts/pdd.sh reconnect-defect` | 16.93 | |

Three cases take about 3.05 seconds each on every run and target:
`TerminalCallbackSuccessorSurvivesOldCleanup` and both
`SequentialRepeatOrRetryUsesFreshServices` cases. Together they are 9.17 of the
9.22 seconds. The other six cases take 0.000 to 0.039 seconds. S5 did not
establish where the 3 seconds go. `WebsocketConnectionHandler.Dispose` has a
3-second bound on the close wait (line 361), but the echo server answers Close, so
that is a lead, not a finding.

### Seeded defects

`./scripts/pdd.sh reconnect-defect` passed: the baseline ran 10 of 10 cases (the 9
reconnect cases and `RecordHostRuntime`), and the `missing-removal` edit failed
`TerminalCallbackSuccessorSurvivesOldCleanup` with "Old service retained
registration". Source identity in `results.json`: `fa96d59`, not dirty, before and
after.

| Seeded defect | Edits | Detected by | Result |
| --- | --- | --- | --- |
| `missing-removal` | Line 290, cleanup in the `Using` disposer | `TerminalCallbackSuccessorSurvivesOldCleanup`, a loopback test with a controlled schedule | Detected |
| Any defect in a decision line (253, 258, 265, 281, 303, 331, 368) | None exists | | Not measured: there is none to run. Stryker does not mutate this file |

The one seeded reconnect defect is in cleanup, which stays in the shell in S6, so it
measures the shell, not the decision.

## Results and recommendation

Not yet written. Step S6 adds the after numbers under
`docs/evidence/reconnect-decision/after/`, the results and the recommendation.
