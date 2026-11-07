# Dafny proof driven development plan for 10.0

Introduce a small, verified protocol core into WebsocketClientLite. Start with masking to establish the C# integration, then prioritize frame validation and fragmentation, where malformed input can change protocol behavior or resource use. Keep networking, Rx subscriptions and platform services in C#.

This is an implementation plan, not a claim that the current library is verified. Initial inspection uses commit `a779602eca143258bc1047a3f7c7faac35b9d7dd`. No runtime changes belong to this planning task.

## What proof will mean

For each selected operation, write its contract from the protocol, prove the Dafny implementation satisfies it, compile that implementation to C#, and call it from the production library. Tests then check the adapters and actual wire behavior. A verified duplicate model would not establish that the existing C# implementation satisfies the same contract.

The [Dafny C# integration guide](https://dafny.org/dafny/DafnyRef/integration-cs/IntegrationCS) describes calling generated methods from C# and mapping namespaces and types. Use an internal generated core with thin C# adapters. Avoid declaring existing protocol algorithms as `extern` and treating their contracts as proven: externally implemented behavior remains an assumption.

Proofs cover specified sequential algorithms under their preconditions. The trusted boundary includes Dafny's verifier/compiler, its runtime, .NET, adapters, stream behavior and cryptographic randomness. Socket progress, thread scheduling, allocation success and TLS security are outside the initial proof scope.

## Protocol scope and priorities

Use [RFC 6455](https://www.rfc-editor.org/rfc/rfc6455.html) as the contract source, recording the section alongside each obligation. Initially model a client connection without negotiated extensions. Explicitly separate protocol requirements from local size limits and compatibility options. Review applicable errata before freezing contracts.

Paths below are relative to `src/main/WebsocketClientLite/`.

| Order | Production code | Contract and proof target | Existing test foundation |
| --- | --- | --- | --- |
| 1 | `Helper/WebsocketMasking.cs`; masking loops in `Service/WebsocketSenderHandler.cs` | Each output byte equals input XOR key at index modulo four; masking twice restores input; accesses stay in bounds; unrelated bytes and the key remain unchanged. RFC 5.3. | `MaskingTests`, `SendFrameTests` |
| 2 | `Helper/DataframeParsing.cs` | Decode network-order lengths exactly; reject invalid headers before payload allocation; accepted sizes fit configured and array bounds. RFC 5.2 and 5.5. | `PayloadParsingTests` |
| 3 | `Service/WebsocketParserHandler.cs` | Legal fragmentation transitions; ordered concatenation; bounded accumulated size; controls preserve message state except Close, which aborts it. RFC 5.4. | `PayloadParsingTests`, `IntegrationTests` |
| 4 | `Service/WebsocketSenderHandler.cs` | Canonical header encoding; correct FIN/opcode selection; masked client output; all writes within buffer bounds and transmitted count equals initialized frame length. RFC 5.2–5.5. | `SendFrameTests`, `ConcurrentWriteTests` |
| 5 | `Parser/HandshakeParser.cs`, `Parser/HandshakeParserDelegate.cs`, `Service/HandshakeHandler.cs` | Pure acceptance predicate over parsed response and request context; success only when all handshake requirements hold. RFC 4.1. | `HandshakeParserTests`, `HandshakeComposeTests` |
| 6 | `Model/Dataframe.cs`, `Service/WebsocketConnectionHandler.cs` | Close payload validity and strict text validation; later, sequential closing transitions. RFC 5.5.1, 7.4 and 8.1. | `DataframeTests`, `TeardownTests`, `ErrorDeliveryStressTests` |

## Behaviors to resolve before proving them

These are findings from inspection, not a completed conformance audit. Turn them into focused regression cases and follow the call paths before changing behavior.

- `ReadDataframeAsync` decodes masked incoming frames, preserves RSV bits and casts arbitrary opcodes without rejecting them there. Define role-aware validation for server masking, reserved bits and unsupported opcodes. Require minimal length encoding and a clear high bit for 64-bit lengths; a size limit is a separate policy check.
- `Reassemble` accepts Text and Binary frames while a fragmented message is open. Specify only Continuation as the next data fragment, reject orphan continuations, and preserve the first opcode. Its returned record replaces Payload and FIN but retains the first frame's Length and Fragment; explicitly define message metadata consistency.
- `DetermineFINBit` returns zero for every Continuation opcode, irrespective of fragment argument. Define supported public argument combinations and prove their mapping, including final continuations. Check outgoing control-frame size and fragmentation validation.
- The sender calculates `headerSize + payloadLength` as `int`. Prove the addition fits before renting a buffer, including payloads near the integer limit through symbolic reasoning rather than enormous test allocations.
- The optional empty-Pong path sends a single opcode byte. Treat that compatibility behavior as outside standard framing guarantees; make the boundary explicit instead of weakening the RFC contract.
- `Dataframe.Message` uses the default UTF-8 decoder. Establish strict validation of a complete text message, including characters split across fragments. Audit handshake checks across the parser and delegate before selecting acceptance conditions; checking status 101 alone is insufficient.

## Implementation sequence

### 1. Establish the integration and masking proof

Add `verification/` for Dafny sources and a contract-to-code index. Pin the Dafny toolchain and bundled solver version. Prototype generation into an internal namespace such as `WebsocketClientLite.Verified`; keep generated files mechanically reproducible and prohibit manual edits.

Use byte bitvectors or bounded native types for wire values. Dafny mathematical integers map to `BigInteger`; prove conversion bounds explicitly wherever .NET array indices or lengths are involved. Check actual generated signatures, runtime dependencies and allocation behavior rather than assuming a sample build supports every library target.

For in-place masking, require a four-byte key and disjoint key/payload storage, or snapshot the key explicitly. Specify the old payload as the reference value. Prove the processed-prefix XOR invariant, untouched suffix, bounds, termination and involution. Extend the contract to sender buffer slices, including nonoverlapping key and payload ranges.

Exit gate: production calls the generated masking implementation, all five library targets build, existing masking/send tests pass, and measured throughput and allocations are compared with the original implementation. Record the acceptable performance budget before choosing the final integration. If compatibility or performance fails, retain the experiment as a model only and do not claim production verification.

### 2. Validate headers before consuming payloads

Extract a pure bounded header decoder with results `NeedMore`, `Reject(reason)` or `Accept(header, consumed)`. Verify that incomplete input causes no out-of-bounds access, rejection cannot authorize payload allocation, and success consumes exactly the header bytes. Include extension/role policy as explicit inputs, initially fixed to the supported client configuration.

Keep exact stream reads in C#. The adapter must enforce the accepted length and avoid exposing partial payloads. Test every header truncation and boundaries 0, 125, 126, 65535, 65536, the configured limit and its successor. Include nonminimal encodings, reserved opcodes, RSV bits, masked server frames, fragmented controls and the 64-bit high bit. Round-trip encoding must use the matching sender role; client output is not valid input for a client-side receive validator.

### 3. Prove fragmentation transitions and bounded assembly

Model Idle, ReceivingText, ReceivingBinary and Closed states. Give every opcode/FIN combination a defined transition or rejection. Prove by induction that accepted fragments concatenate in order, total length equals the sum of segment lengths and remains within the message limit, and a message emits only on completion. Ping/Pong do not alter the partial message; Close discards it and prevents later data delivery.

Generate the transition function for production use. Separately verify copy offsets, or retain C# concatenation with an explicit unverified adapter boundary and tests. Exercise controls between fragments, empty fragments, interrupted input and invalid sequences. Do not label the whole reassembler verified while copying or metadata updates remain outside the proof.

### 4. Prove outgoing framing and integrate remaining validation

Extract header sizing, encoding and FIN/opcode selection. Prove canonical lengths and buffer region bounds, and reuse the masking kernel. Keep randomness, pool ownership and asynchronous writes in C#. Test cancellation, exceptions, pool return and concurrent sends; frame serialization alone does not prove that two fragmented messages cannot interleave.

Then add pure handshake and close-validation predicates. Keep HTTP parsing, SHA-1/Base64 implementations and UTF-8 decoding as documented dependencies unless separately verified. A proof over a boolean supplied by an external decoder establishes conditional validity only. Defer asynchronous teardown proofs until the core has demonstrated value; existing stress tests remain necessary.

## Proof driven change workflow

For each increment:

1. Record the RFC obligation, executable contract, assumptions and affected production caller.
2. Add a failing regression example for any observed discrepancy. Define desired behavior from the specification rather than preserving a bug as a precondition.
3. Implement and verify the Dafny code. Include rejection behavior for arbitrary wire input; do not require untrusted inputs to already be valid.
4. Generate C#, integrate the adapter and test wire behavior against independent expected bytes and invalid-input cases.
5. Review the contract, proof and adapter together. Record precisely what is proven, tested or assumed.

Maintain an evidence table with columns for requirement, Dafny symbol, production caller, boundary assumptions, regression tests and CI result. No unchecked assumptions, axioms or verification-disabled methods may silently discharge a protocol obligation. Review external contracts explicitly.

## CI and acceptance

Add a separate verification job that fails on proof errors or timeouts, checks the pinned toolchain, regenerates C# and detects stale output. Archive verification logs and tool versions. Keep generated output tied to the exact verified source revision; compilation alone is not proof.

Continue `dotnet build src/main/WebsocketClientLite.sln`, run `./test-all-tfms.sh` in the repository's Release configuration and preserve the existing 70% line-coverage gate via `scripts/check-coverage.sh`. Decide and document treatment of generated source in coverage without using it to dilute coverage of handwritten adapters. Test package consumption on all five targets before merging runtime integration.

The first deliverable is complete when the masking implementation used in production has a reproducible proof, its adapter assumptions are explicit, all target-framework checks pass and measured costs are acceptable. Frame validation and fragmentation are the next required milestones for meaningful protocol assurance. Full RFC compliance and concurrency correctness remain separate claims requiring additional evidence.
