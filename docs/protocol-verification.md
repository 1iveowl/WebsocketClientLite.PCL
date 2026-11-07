# Protocol verification in version 10.0.0

Version 10.0.0 adds a verified synchronous core to WebsocketClientLite. Critical frame parsing, fragmentation decisions, buffer sizing, header encoding and masking operations are written in Dafny and compiled to C#. The library calls that generated code in production.

This work also corrects protocol behavior: malformed server frames are rejected before payload allocation, fragmented messages follow an explicit state machine, and a connection is accepted only after validating the server's handshake against the request that was actually sent.

## What proof driven development means here

Tests check selected examples. Dafny checks whether an implementation satisfies its written contract for every input allowed by that contract. For example, the masking contract states which XOR result every output byte must contain, which bytes must remain unchanged, and which array bounds must hold.

The process is to state the intended behavior, prove the implementation against it, generate C#, and test the integration. A proof of a separate model would leave a gap between the model and the running code; using generated code connects the verified algorithm to the library.

The contracts express the relevant rules from [RFC 6455](https://www.rfc-editor.org/rfc/rfc6455.html). They still need human review: proving that code satisfies a specification does not prove that the specification correctly captures every requirement of the RFC.

## What we proved

These guarantees hold under the methods' stated preconditions, including valid buffer ranges and no concurrent mutation of their arrays.

| Area | Proven property |
| --- | --- |
| Incoming frame headers | The decoder distinguishes incomplete, rejected and accepted headers without reading unavailable extended-length bytes. Acceptance requires supported framing, canonical length encoding and a payload length within both the configured limit and the array-size bound. |
| Length decoding | Extended lengths equal the big-endian numeric value of the supplied bytes. |
| Fragmentation decisions | The state machine accepts only its defined opcode/state transitions. Rejection leaves state unchanged. Accepted accumulated sizes stay within the limit. Ping/Pong preserve a pending message; Close terminates it. A data message completes only on FIN. |
| Outgoing frame sizing | Accepted frames have the canonical header size, valid FIN/opcode mapping and control-frame constraints. Header plus payload fits the supported integer range. |
| Header encoding | The generated writer produces the specified marker and network-order length bytes while preserving the key and payload region. |
| Outgoing masking | Every destination byte has the specified XOR value. Copy-and-mask preserves the source; both masking operations preserve bytes outside their destination slice. Array accesses stay in bounds and the loops terminate. In-place masking also preserves its key region. |

Handshake and Close acceptance are implemented as Dafny predicates. The handshake predicate requires every supplied acceptance condition; the Close predicate checks length, allowed status codes and the supplied UTF-8-validity result. These predicates make the acceptance rules explicit and executable. They do **not** prove the C# computations that supply those conditions, such as calculating the accept hash or decoding UTF-8.

Encoding and decoding are tested together against independent byte expectations. There is no combined formal theorem proving that the encoder and decoder are inverses.

## What improved

**Invalid frames fail before payload allocation.** The client now rejects masked server frames, unsupported reserved bits and opcodes, nonminimal length encodings, invalid control frames and oversized lengths. Protocol constraints and resource limits are checked together before reading the payload into a new buffer.

**Fragmented messages follow explicit rules.** A continuation cannot arrive without a pending message, and a new Text or Binary frame cannot replace an unfinished message. Interleaved Ping/Pong frames preserve reassembly state; Close discards the unfinished message. Completed messages have consistent length and fragment metadata. UTF-8 validation runs after text reassembly, so a valid character may span several frames.

**Handshake success means more than HTTP status 101.** The server's accept value is checked against the random key sent in that request. The client also checks the HTTP version, Upgrade/Connection tokens and selected subprotocol. An unoffered or multiple subprotocol selection is rejected; omitting selection is allowed. Unsupported extensions are rejected, and custom headers cannot override protocol-owned request fields. Invalid responses fail when their headers finish, even if the server leaves the connection open.

**Outgoing framing and Close handling are stricter.** Size arithmetic is checked before renting a frame buffer. Outgoing control frames must satisfy their size and fragmentation constraints, and an explicit final continuation now sets FIN correctly. Close payloads reject one-byte bodies, forbidden status codes and invalid UTF-8 reasons. String encoding rejects invalid UTF-16 input instead of silently replacing it.

**Future changes have a repeatable check.** CI is configured to run the pinned verifier and compare regenerated C# with the checked-in output. A code change that violates a contract fails verification; stale generated code fails the comparison. The existing build, tests and coverage gate remain in place.

## What remains outside the proofs

Dafny does not directly verify the client's C# async/await code. The following remain reviewed or tested boundaries:

- Socket reads and writes, cancellation, scheduling, Rx delivery and disposal races.
- Applying state transitions from C#, retaining and concatenating fragments, and delivering the completed message.
- ArrayPool ownership and buffer lifetime across asynchronous writes.
- HTTP parsing, header/token interpretation, SHA-1/Base64, random key generation and UTF-8 decoding.
- Closing-handshake timing, close-code selection and independently initiated fragmented sends that may interleave.

The optional legacy single-byte empty-Pong behavior remains outside standard framing guarantees. The work is not a proof of the entire client or a claim of complete RFC conformance. The verifier, compiler, runtime, .NET and the adapters that establish preconditions remain trusted components.

## Validation and performance

Local validation of this implementation produced:

- **23 successful Dafny verification results**, with no errors, followed by a successful generated-source consistency check. This number includes specification well-formedness checks; it is not a count of independently proved RFC rules.
- **161 passing tests per library target**, across all five targets, using a .NET 10 test host.
- **79.9% line coverage**, above the existing 70% requirement, with generated source included.
- Successful Release builds and independent package-consumer builds for every target framework.

A deliberately incorrect XOR-to-OR change in copy-and-mask failed its output contract, confirming that the proof detects that implementation error. Remote CI still needs to run when the changes are pushed.

Local .NET 10.0.14 microbenchmarks measured zero allocations in the active masking loops. Fused copy-and-mask was approximately 10–12% faster than the comparable scalar loop; in-place masking was approximately 6% faster, for the measured 125-byte, 1 KiB and 64 KiB payloads. These are kernel measurements on one environment, not end-to-end network speed guarantees.

The retained whole-array masking pilot helper is no longer used by the receive path, because masked server frames are rejected. Its final benchmark was slower than its original scalar comparison. The [verification guide](../verification/README.md) records that result alongside the active sender measurements and explains how to reproduce them.

## Using and maintaining the library

Consumers do not need to install Dafny or Z3. Generated C# is built into the library; version 10.0.0 adds the `DafnyRuntime` 4.11.0 package dependency. All existing target frameworks remain supported.

The stricter validation can reject malformed traffic or custom handshake headers that earlier versions accepted. Applications relying on those behaviors should check their server responses and connection options when upgrading.

For source files, exact contracts, trusted boundaries, toolchain versions and regeneration commands, see the [verification guide](../verification/README.md). The [original adoption plan](dafny-pdd-plan.md) records the intended scope and sequence.
