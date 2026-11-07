# Increment G: Close evidence reliability

This document records the design and retained original failure, not human approval.
See [the validation and diagnosis record](../docs/increment-g-validation.md) for
measurements and the limits of the historical timeout reproduction.

## Original failure retained

Release run [42401715327](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/runs/42401715327)
tested `f3d5ed4d336c2c7f1df88643fb100db76acb0cc4`. D2/E/F are present.
The four execution jobs succeeded; evidence-gate correctly rejected the critical
NotifyReadFailure deletion with raw outcome **Timeout**.

The original `pdd-receipt` artifact (13585582784) is retained verbatim in
[evidence/increment-g/original-receipt.zip](evidence/increment-g/original-receipt.zip).
SHA-256: `5418da80e8f790a52edc5cb7624f1b2cb1e574282613576784ae4eb81e3dd5fa`.
It contains source identities, command/tool/runtime records, the raw mutation
report and failed aggregate. Its artifacts belong only to the original revision;
none are relabeled as G validation.

Identity: `Service/WebsocketConnectionHandler.cs`, source SHA-256
`279760e2e0e05b1a2ab3d15348ef3e6d0d417b941de93cae088221a9abb0bdc3`,
Statement mutation replacing `sender.NotifyReadFailure();` with `;`, original
location 184:21–184:48. Fingerprint
`2948746ba70d8f2b5bac5cdec0abba0701a3f0e7d3d648082ffffedb6253dd93`.
The report-local ID 590 is explanatory only. Original Stryker execution: 490.54 s.

## Intended boundaries and test design

`ObserveFrames` extracts the existing async Observable.Create body into an
internal task-returning method. This lets cancellation tests await the actual
finally path, rather than poll for absence of a close. Parser/control projection
and production terminal routing are unchanged. The internal ping scheduler
argument defaults to the same Rx default scheduler; tests use virtual time.
No public API or intended runtime correction is included.

| Gap | Schedule and assertion | Isolated defect |
| --- | --- | --- |
| Read failure readiness | inject error; await pipeline task with no expiring peer delay; readiness must already be released | remove NotifyReadFailure |
| Parser fallback | source completes; pause write; observer must wait; resume and peer Close permit completion | remove fallback await |
| Cancellation condition | cancel a Never source; await pipeline task; zero fallback calls/terminals; explicit disposal then sends Close | invert finally condition |
| Observer error | inject original exception; record Aborted, observer error, fallback order and exact exception identity | remove observer OnError |
| Observer completion | complete source; await task; exactly one observer completion follows close | remove explicit OnCompleted |
| Periodic cleanup | advance virtual interval to send Ping; dispose; advance again; no new scheduling or SendError | remove async/sync ping disposal separately |
| Active ping write | pause a periodic Ping; dispose; its token must be cancelled without resuming the write; transport releases without Close retry | remove async ping disposal |
| Calling context | pause Close; call DisposeAsync under a context that holds posts; resume from outside; disposal completes without pumping | capture context on DisposeAsync await |

The direct lifecycle completion assertion covers the internal method's contract;
Rx also completes an async Create subscription when its task returns. This is
not a claim that removal of the redundant outer completion would always affect
public subscribers. Real parser/loopback tests remain mandatory.

Diagnostics use three-second bounds, not sleeps to establish ordering. Cleanup
releases every held write/delay, notifies failure, cancels the subscription and
bounds disposal, then releases socket resources and prints the event sequence.
The custom context is drained only in cleanup, after the assertion checkpoint.

The independent `boundary-defects` command copies only repository inputs to a
temporary tree. It requires a passing baseline and the named assertion in TRX
for every isolated edit; compile failures, timeouts and unrelated errors fail.
NotifyReadFailure is repeated in five separate test-host processes. The original
ordering experiment and all seven critical Stryker obligations remain independent.

## Scope and review

These are selected schedules on five library targets with the actual .NET host
recorded, not five separately tested runtimes. Custom contexts in handshake,
Ping/Pong processing and arbitrary external callbacks remain unproved/partially
untested. Native-copy trust, verifier/compiler/runtime trust and exhaustive
asynchronous verification remain unchanged. Semantic acceptance needs a human
review of the actual revision; no approval is recorded here.
