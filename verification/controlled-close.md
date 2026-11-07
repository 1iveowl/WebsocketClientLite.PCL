# Controlled Close schedules (increment E)

The tests in `ControlledCloseTests.cs` specify selected interleavings. They do not
prove all scheduler behavior or external stream/callback progress. Existing real
loopback tests remain in `TeardownTests.cs` and run in the same matrix.

| Schedule | Explicit sequence | Required observation |
| --- | --- | --- |
| Paused Close, peer first | peer Close → start local Close → write entered → two disposals → resume | both disposals pending and stream writable before resume; one masked GoingAway frame; completion before Disconnected |
| Paused Close, local first | start local Close → write entered → peer Close → two disposals → resume | same observations, including exact unmasked status/reason |
| Peer timeout | disposal → paused write → resume → peer delay entered → expire injected delay | disposal pending until expiry, then transport released |
| Read failure in peer wait | same sequence through delay entry → NotifyReadFailure | disposal completes without waiting for expiry |
| Failed Close write | disposal → write entered → resume with IOException | disposal completes, no retry, no peer wait; attempted bytes are not claimed delivered |
| Cancellation between fragments | first fragment write entered → cancel → resume successful first write | no continuation or later data; Close still allowed |
| Queued Pong | paused local Close → queue Pong at held frame gate → peer Close → resume | Pong suppressed, only Close written |
| Post-write failure | pause data write → queue data → resume with IOException → attempt later data → dispose | queued and later sends rejected; no Close appended to unknown wire prefix |

The harness uses the existing injected write delegate and frame serialization.
Reflection installs a memory transport and the existing shared close task,
following the precedent in `ProtocolStateTests`. It exercises production
DisconnectWebsocket/DisposeAsync, but does not replace the loopback coverage of
handshake and parser registration. The only new production seam is an optional
internal peer-delay delegate; its default remains `Task.Delay`. No public API
changes or runtime corrections were required by these schedules.

All barriers use asynchronous continuations. Diagnostic waits are bounded at
15 seconds, never used to establish ordering. Every test releases barriers in
cleanup, signals terminal state and bounds task draining; event sequences are
printed even on assertion failure. Cleanup faults from deliberately failed sends
are observed. Normally synchronous cleanup sees an already-created close task. If setup or an
early assertion fails first, its existing three-second bound still applies.

## Independent ordering experiment

`./scripts/pdd.sh controlled-defect --output <fresh-directory>` copies repository
inputs into a temporary directory, runs the two paused-write cases plus the host
runtime probe, and replaces exactly one `await closeOnce.Value.ConfigureAwait(false)`
with a discarded task in the copy. Both cases must fail on the named ordering
assertion, after a passing baseline. Compile failures, missing cases, unexpected
failures, timeouts and process termination do not count as detection. The copied
sources are deleted; production files are never mutated in place. JSON records
the original/mutant hashes, command receipts, runtime, source identity and CI
identity; TRX and logs remain alongside it.

`CLOSE-004-CONTROLLED` requires the matrix tests and this separate experiment.
`CLOSE-005-CONTROLLED` requires its named controlled tests on all five library
builds. The actual test host is recorded; five library builds are not five runtime
versions. Stryker evidence is separate and maps to both requirements. PR semantic
review remains unexecuted until a reviewer actually records acceptance.

The additional `ParserSignalsPeerCloseOrReadFailureBeforeTimerExpiry` loopback
schedule waits for the local Close and entry to an injected, unexpired peer timer,
then permits either a peer Close or a frame exceeding the configured receive
limit. The corresponding status callback follows the production parser's sender
notification synchronously. A fresh waiter must immediately observe readiness;
disposal must finish without timer expiry. This closes the pilot's missing tests
for removal of `NotifyPeerClose` and `NotifyReadFailure` in the connection handler.

## Increment G

`CloseBoundaryTests` and the independent `boundary-defects` command extend the
selected schedules to parser completion/cancellation, explicit observer routing,
periodic-ping cleanup and a non-pumping calling SynchronizationContext. See the
[G evidence/design record](close-evidence-g.md) for exact causal checkpoints,
source fingerprints, measured reproductions and remaining exclusions. The
controlled CI job captures both experiment sets; neither substitutes for the
other or for the independent critical Stryker gate.
