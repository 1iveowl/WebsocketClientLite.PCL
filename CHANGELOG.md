# Release notes

One section per release, newest first. The publish workflow uses the section `## New in Version <x.y.z>` for the version being released as the text of its GitHub release, so keep that heading format. The README has a one-line summary of each release in its release history table.


## New in Version 10.5.0

Version 10.5.0 changes no public API, no library behaviour and no Dafny contract. It records what happens when the header length contract itself is wrong, writes down the rules the verification work follows, and turns one of them into a check.

- **A weakened contract still verifies.** [`verification/contract-weakening.md`](verification/contract-weakening.md) weakens the two length bounds of `HeaderValid`, the RFC 6455 section 5.2 rule that a length uses the fewest bytes. With either bound weakened the verifier still reports `41 verified, 0 errors`, because the decoder calls the contract and changes with it. The generated-code check fails, but only because the C# was not regenerated. After regenerating, the tests built from the RFC catch both: two tests fail on one row each, on all six targets, and nothing else fails. The header mutant written for the weakened bound survives. No open gap was found for these two bounds. The weakened contract is not in the shipped code or the mutation catalog; the patches, commands and outputs are in [`docs/evidence/contract-weakening/`](docs/evidence/contract-weakening/).
- **Rules for verification work.** [`verification/README.md`](verification/README.md#rules-for-verification-work) states three rules: when a requirement is in question, the RFC wins; expected bytes never come from production code; a new test is seen to fail on the old code, and that run is recorded, before its fix is trusted. `AGENTS.md` points to them and to the boundary register.
- **RFC citations are checked.** `./scripts/pdd.sh validate` fails when a declaration in the Dafny sources has neither an RFC 6455 section nor a stated "Library policy:" in the comment directly above it, or a requirement's origin has neither. A declaration form the check does not read fails too, so none is skipped. Its first run found two masking lemmas without a citation; they now cite section 5.3. The comments are not embedded in the generated C#, so `Masking.g.cs` is unchanged. The rules are in [`tools/Pdd/README.md`](tools/Pdd/README.md) and the runs in [`docs/evidence/citation-check/`](docs/evidence/citation-check/).
- The exploration harness (`verification/exploration/run.py`) starts its `net11.0` build and runs again, and the README's .NET badge and the mutation and Stryker guides name .NET 11. The 10.4 bump had missed them.

No new public API and no new production dependency. The library's C# and the generated code are the same as in 10.4.0. The [10.5 plan](docs/pdd-10.5-plan.md) has the steps, decisions and lessons.

## New in Version 10.4.0

Version 10.4.0 adds .NET 11 and changes no public API or documented behaviour. It is the first release built with the .NET 11 SDK.

- **New target: `net11.0`.** The package now has six builds: .NET Standard 2.0 and 2.1, .NET 8, .NET 9, .NET 10 and .NET 11. .NET 8 and .NET 9 reached end of support on 2026-11-10; their builds stay, so nobody on them is moved to a different build in a release that promises no behaviour change.
- **The .NET 11 build uses Runtime Async.** On .NET 11 the runtime, not compiler-generated state machines, suspends and resumes the library's async methods. The `net11.0` assembly has none of the 48 state machines left and is 102 KiB instead of 136 KiB. In an end-to-end loopback benchmark every measured path allocates 216 to 351 bytes less per operation, and none is measurably slower. Live stack traces and debugger call stacks show fewer frames; exception stack traces are unchanged. The other five builds are compiled as before. The report is [`verification/runtime-async.md`](verification/runtime-async.md).
- The library is compiled with C# 15 (`LangVersion` 15) but does not yet use C# 15 syntax. A union for the internal handshake result is written and tested, but Stryker.NET, which runs the mutation checks for every release, cannot compile C# 15 yet. It waits for a Stryker release that can. The [10.4 plan](docs/net11-10.4-plan.md) has the details.
- `Microsoft.Bcl.AsyncInterfaces` stays on 10.0.x: version 11.0.0 warns that it does not support .NET 8 or .NET 9, and the interface package would pass that warning to every .NET 8 and 9 build that uses it.
- Tests, tooling, CI and the dev container use the .NET 11 SDK. All six library builds are tested on a .NET 11 host, and the controlled ordering experiments and Stryker run against the `net11.0` build.

No new public API and no new production dependency. The Dafny proofs, their scope and the generated code are unchanged. The evidence and its limits are in the [10.4 plan](docs/net11-10.4-plan.md).

## New in Version 10.3.0

Version 10.3.0 adds no feature and changes no public behaviour. It ties the header length rule to RFC 6455, makes the header tests fail when the check they name is removed, and records two experiments about the boundary between the C# client and the verified protocol core. One is adopted and one is not.

- Every contract-bearing method and lemma in the Dafny core now names the RFC 6455 section it implements, or says that it is library policy. The comments are not embedded in the generated C#, so `Masking.g.cs` is unchanged.
- The rule that a frame length must use the fewest bytes (RFC 6455 section 5.2) is a traced requirement (`HEADER-001`, with `HEADER-002` for refusing a frame from its header before any payload is read) with two Dafny mutants. The test for it used to pass on a parser that accepted non-minimal lengths, because the frames carried no payload and the old parser threw at end of stream. Tests now send the payload a header announces, so an accepting parser fails on its own. The [header length note](docs/header-length-validation.md) has the replay.
- Test parity for the header rules: a row for every reserved value (RSV2, RSV3, opcodes 4 to 7 and 11 to 15), a frame of exactly the configured limit is read and one byte more is refused from its header, and a truncated header ends in the end-of-stream error at every prefix. The [validation note](docs/header-test-parity-validation.md) lists every claim, its test, its failing-first run and what stays unbacked.
- A test for a rejected handshake awaits the connect call and checks that no sender is installed. It replaces a kill that depended on a race: the Stryker mutant that removes `throw handshakeException;` was only caught when a test happened to run first.
- **Adopted:** named types at the verified core boundary. The receive, transmit and close states and the close event are small internal structs that cannot hold a value outside the range the generated core requires, so an out-of-range code cannot be passed from C#. The measured per-frame paths allocate the same number of bytes and show no measurable change in time. The report is [`verification/core-boundary-types.md`](verification/core-boundary-types.md).
- **Not adopted:** the reconnect lifecycle decision as a pure function. It was built, measured and reverted. It mirrored the six lock sites one to one and removed nothing from the expensive part of the tests, so the production change is not in this release. The report and its evidence stay: [`verification/reconnect-decision.md`](verification/reconnect-decision.md).
- The Pdd tooling reads Dafny declarations more carefully (attributes, bodiless functions, set displays in contracts), and its fixtures name a declaration and an offset instead of an absolute line, so a comment in `Protocol.dfy` no longer breaks eleven tests.

All five library targets remain supported. These changes add no public API and no new production dependency. The only production C# changes are the internal boundary types above and one internal constant for the end-of-stream message. The Dafny proofs retain their scope: the new mutants show that the contract catches a decoder that accepts one refused header, not that the whole decoder is covered. See the [verification guide](verification/README.md) and the [10.3 session plan](docs/pdd-10.3-session-plan.md) for the evidence and its limits.

## New in Version 10.2.0

Version 10.2.0 strengthens cancellation, teardown and sequential reconnect behavior, and expands the evidence for the C# boundaries around the verified protocol core.

- Transport abort wakes queued writers so they can settle instead of remaining blocked behind a disposed gate. Controlled tests cover cancellation before admission, queued writes and in-flight ownership.
- Sequential reconnect cleanup respects connection ownership. Late cleanup from an old connection cannot clear the current sender, and disposal prevents new connection admission.
- Tests cover payload buffer lifetime and once-only return, parser retention and metadata, scratch-buffer ownership, Close ordering, observer terminal signals and periodic Ping cleanup.
- Verification receipts bind tests, proof checks and mutation results to their source and runtime. Missing, stale, interrupted or timed-out evidence fails acceptance; bounded process cleanup retains diagnostic output.

All five library targets remain supported. These changes add no public API or new production dependency. The Dafny proofs retain their existing scope; asynchronous ownership and lifecycle behavior are tested C# boundaries, not proofs of arbitrary schedules or network progress. See the [Phase 3 validation record](docs/p3-g-validation.md) for repeatability, costs and remaining platform qualification limits.

## New in Version 10.1.0

Version 10.1.0 extends the Dafny core with receive admission that includes fragmentation state, outgoing message transitions, closing transitions, exact segment concatenation, and a length-header round-trip theorem.

- List sends hold the data-message gate until their final fragment; Ping/Pong can still interleave. All list items are validated before the first write. Cancellation after a partial list prevents further data sends; an uncertain partial wire write prevents all further frames.
- Orphan continuations, conflicting data frames and aggregate-size violations fail from the header, before payload allocation or reading.
- Manual sends reject invalid message sequences. Callers still own the sequencing of manually supplied continuations and must not mutate byte arrays during a send.
- Asynchronous disposal waits for the peer's Close response, with a five-second response timeout after the bounded Close send. Synchronous disposal remains bounded, best-effort teardown.

The proofs cover synchronous decisions and byte operations under explicit preconditions. C# lock ownership, socket progress, cancellation and event delivery remain reviewed and tested boundaries. See the [verification guide](verification/README.md) for the contracts and evidence.

## New in Version 10.0.0

Version 10.0.0 introduces **proof driven development with Dafny** for critical WebSocket protocol logic. The library runs C# generated from verified implementations of header parsing, fragmentation decisions, frame sizing, header encoding and masking, with explicit handshake and Close acceptance predicates.

**What this means**

The verified algorithms are checked against written contracts for every input allowed by those contracts, complementing the examples covered by tests. This gives stronger assurance about byte transformations, bounds and protocol state transitions. CI is configured to repeat verification and detect stale generated code when those implementations change.

**Protocol improvements**

- **Reject malformed frames before payload allocation.** Validate server masking, reserved bits/opcodes, canonical lengths, control-frame constraints and size limits.
- **Handle fragmentation consistently.** Reject orphan continuations and new data frames during unfinished messages; preserve state across Ping/Pong; correct reassembled metadata and validate complete text as strict UTF-8.
- **Validate the handshake before reporting success.** Check the accept value against the actual request key, Upgrade/Connection tokens and subprotocol selection. Reject unsupported extensions and overrides of protocol-owned request headers.
- **Strengthen outgoing framing and Close validation.** Check size arithmetic, set FIN correctly on explicit final continuations, reject invalid control frames and Close payloads, and reject invalid UTF-16 strings during encoding.

**Compatibility and dependencies**

All five target frameworks remain supported. Consumers do not need Dafny or Z3; the library adds a dependency on **DafnyRuntime 4.11.0**. Stricter validation may reject malformed traffic or custom handshake headers accepted by earlier versions.

The proofs cover the synchronous core, not the entire asynchronous client. Local validation passed 161 tests against each library target and achieved 79.9% line coverage. Active sender masking benchmarks measured zero allocations and improved throughput; these are local kernel measurements, not network performance guarantees.

Read [what we proved, what improved and where the proof stops](docs/protocol-verification.md) for the guarantees, limitations, upgrade implications and validation results.

## New in Version 9.2.0

Version 9.2.0 updates the package dependencies.

**Dependencies**

- **System.Reactive 7.0.** The library now depends on System.Reactive 7.0.0 (previously 6.1.0), so projects that reference this package move to Rx 7.
- HttpMachine.PCL 6.2.0, and Microsoft.Bcl.AsyncInterfaces 10.0.14 on `netstandard2.0`.

## New in Version 9.1.0

Version 9.1.0 unifies connection teardown, adds an asynchronous disposal path, and upgrades the handshake parser.

**New**

- **`IAsyncDisposable` on `ClientWebSocketRx` (and the legacy `MessageWebsocketRx`).** `await using` is now the preferred lifecycle: `DisposeAsync()` awaits the RFC 6455 close handshake of any still-active connection before releasing the socket. `Dispose()` remains the abrupt counterpart — a bounded, best-effort close (up to 3 seconds) so synchronous disposal can never hang. The `IWebSocketClientRx` interface now extends `IAsyncDisposable`.

**Bug fixes and internals**

- **Unified teardown.** The close handshake now runs exactly once on every end path — server close, read error, unsubscribe, `Dispose`, `DisposeAsync` — through one shared mechanism, replacing the previous split between an Rx operator and a disposal-path workaround. Every teardown path can await (or boundedly wait for) an in-flight close, so the close frame always gets its chance to go out before the socket is torn down.
- **Read-loop errors reliably reach the subscriber.** A mid-connection error (oversized frame, protocol violation, dropped connection) previously raced the teardown's `Disconnected`/completion and could be swallowed into a graceful completion; it now always surfaces as `OnError` carrying the original exception.
- **Removed `ObservableExtensions.FinallyAsync`** (public, but effectively internal). The operator was unsound: it fired on completion and error but never on unsubscribe — the most common way to leave a hot stream — and could replace the stream's error with a cleanup error. Its role is covered by the unified teardown above.
- **HttpMachine.PCL 6.0.1.** The handshake response is now parsed via the new span-based `Execute` overload using a shared scratch buffer — no per-byte allocations during the handshake. Header lookups are case-insensitive (the subprotocol negotiation no longer depends on the parser's header-name normalization). The 6.0.1 parser also fixes several HTTP conformance issues upstream.

## New in Version 9.0.2

Fixes a race in the 9.0.1 close-on-unsubscribe behavior: disposing the connection subscription immediately after `WebsocketConnected` could tear the connection down before the close-handshake hook was wired, skipping the close frame. The hook is now registered before `WebsocketConnected` is emitted and runs on every teardown path. Use 9.0.2 instead of 9.0.1.

## New in Version 9.0.1

Version 9.0.1 is a correctness and robustness patch on top of 9.0.

**Bug fixes**

- **Unsubscribing now sends a close handshake.** Disposing the connection subscription previously dropped the TCP connection without an RFC 6455 close frame; the close is now sent best-effort on every teardown path, exactly once.
- **Failed handshakes fail fast.** A non-101 response (e.g. 404) now surfaces immediately as an error carrying the server's status code, instead of reporting a misleading internal "success" state and waiting out the handshake timeout.
- **Send failures throw.** `await sender.SendText(...)` (and all other sends) now throws `WebsocketClientLiteException` when the write fails, in addition to reporting `SendError` on the status channel. Previously failures were silent at the call site.
- **The final `Disconnected` status is delivered.** An Rx-grammar bug completed the status stream before emitting the last status.
- **NuGet packaging:** the interface assembly is no longer bundled into `lib/netstandard2.0/2.1` alongside the `IWebsocketClientLite` package dependency (which could cause duplicate-assembly/type-conflict issues).
- **Reconnect guidance:** supplying your own `TcpClient` only works for the first connection; the sample and docs now reflect that reconnect patterns require leaving `TcpClient` unset. See the note under *Creating a WebSocket Client*.
- Disposing the client while a connection is active no longer throws from teardown; a `Close` received mid-fragmentation now cleanly aborts the message; per-frame header reads no longer allocate.

**New**

- **`NegotiatedSubprotocols`** on `ClientWebSocketRx` exposes the subprotocol(s) the server accepted during the handshake.

## New in Version 9.0

Version 9.0 is a correctness and protocol-compliance release.

**Bug fixes**

- **Subprotocols are now sent.** The `Subprotocols` you configure are now included in the `Sec-WebSocket-Protocol` handshake header. Previously they were only used to validate the server's response and never actually sent.
- **Close codes use network byte order.** WebSocket close status codes are now sent big-endian as required by [RFC 6455 §5.5.1](https://tools.ietf.org/html/rfc6455#section-5.5.1); they were previously byte-swapped on little-endian platforms.
- **`ExcludeZeroApplicationDataInPong` now works.** The flag is honored when replying to a zero-length ping (required for Slack's RTM API). Previously it had no effect.
- **`CancellationToken` is honored.** The token passed to `WebsocketConnectObservable` is now forwarded to the underlying connection. Previously it was ignored on that overload.
- **More robust teardown.** Client-ping failures no longer surface as an unhandled exception on a background thread, a disposed read stream no longer yields a bogus empty frame, a connection closed mid-handshake now reports a clear error, and per-connection resources are disposed correctly.

**Security hardening**

- **Incoming frame/message size cap.** A new `MaxFrameSize` property (default 16 MiB) bounds both a single incoming frame and a fully reassembled fragmented message, preventing a malicious server from exhausting memory. See [Limiting incoming message size](#limiting-incoming-message-size).
- **Control-frame validation.** Control frames (Close/Ping/Pong) that are fragmented or exceed 125 bytes are now rejected per [RFC 6455 §5.5](https://tools.ietf.org/html/rfc6455#section-5.5).
- **Handshake header-injection guard.** Headers, `Origin`, and subprotocol values containing CR, LF, or NUL are now rejected, preventing HTTP header injection / request smuggling.
- **Certificate revocation checking.** A new `CheckCertificateRevocation` property (default `true`) enables TLS revocation checking. See [Certificate revocation](#certificate-revocation).

**Breaking changes**

- `CheckCertificateRevocation` now defaults to `true` (TLS revocation checking is performed). If your environment cannot reach the certificate's OCSP/CRL endpoints, set it to `false`.

- `ClientWebSocketRx` is now a `class` instead of a `record`. A connection holds live, disposable state, so value-equality and `with` semantics were both misleading and incorrect.
- `ClientWebSocketRx.TcpClient` is now nullable (`TcpClient?`). Leave it unset to have the client create one with the address family matching the target URI and dispose it automatically, or supply your own and control its lifetime via `HasTransferSocketLifeCycleOwnership`.

**Internal cleanup**

- Removed dead code (unused masking helpers, an unused scheduler, a duplicate property) and an extra per-frame buffer allocation on the send path.

## New in Version 8.0

Version 8.0 includes several significant improvements:

- Class name change: Renamed from `MessageWebsocketRx` to `ClientWebSocketRx` to better reflect its purpose.
- Improved TCP socket lifecycle management.
- Enhanced error handling and connection stability.
- Updated to use modern C# features.
- Performance optimizations throughout the codebase.
- Explicit TCP socket lifecycle ownership control.

### TCP Socket Lifecycle Management

Version 8.0 provides better control over the TCP socket lifecycle through the `HasTransferSocketLifeCycleOwnership` property:

```cs
var client = new ClientWebSocketRx
	{
		TcpClient = tcpClient, HasTransferSocketLifeCycleOwnership = true
	};  // When true, the WebSocket client will dispose the TCP client 
```

When set to `true`, the WebSocket client will take ownership of disposing the TCP client when the WebSocket client is disposed.
