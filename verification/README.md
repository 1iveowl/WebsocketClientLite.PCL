# Verified WebSocket protocol core

The library calls C# generated from Dafny for synchronous protocol decisions and byte operations. `Masking.dfy` contains the original whole-array masking pilot and includes `Protocol.dfy`, which implements the remaining core. `generated/Masking.g.cs` contains both modules. Generated types are internal.

This work implements the five stages in the [adoption plan](../docs/dafny-pdd-plan.md): header validation, fragmentation, outgoing framing/masking, handshake/Close predicates, and integration checks. It does not establish full RFC conformance or prove the asynchronous client.

## Contracts used by production

| Area | Dafny symbol and guarantee | C# caller and remaining boundary |
| --- | --- | --- |
| Header parsing | `DecodeHeader`: NeedMore/Reject/Accept and bounded canonical lengths. `Admit` composes decoding with `Step`, requiring a legal message transition and a bounded aggregate before accepting a header. `ReadLength` equals a big-endian byte fold. | `DataframeParsing.ReadDataframeAsync` owns stream reads, cancellation and payload allocation. It supplies the current message state/total, reads only the requested extended bytes, and allocates/reads a payload only after admission. Stream reads and ordering of these C# calls are not proved. |
| Fragmentation | `Step`: exact legal state/opcode transitions; rejection preserves state; accepted accumulated sizes remain bounded; controls preserve pending state except Close; completion requires FIN. | `WebsocketParserHandler` applies each transition and retains segments. Generated `Merge` proves that the new buffer equals the ordered concatenation of all supplied arrays and leaves inputs unchanged, assuming the explicit `NativeCopy.CopySlice` contract for .NET bulk copy. `MergeScalar` independently proves the per-byte algorithm without that assumption. The C# invariant connecting retained arrays to the transition total, metadata updates and observer emission remains reviewed/tested. |
| Outgoing frame sizing | `Outgoing`: canonical header size, bounded total size, defined FIN/opcode mapping and control-frame constraints. | `WebsocketSenderHandler` validates enum ranges before conversion, rents the buffer and uses the returned sizes. |
| Header encoding | `WriteHeader`: exact marker and network-order length bytes; key/payload region unchanged. | Sender supplies the validated sizes. `HeaderRoundTrip` calls the actual encoder and decoder and proves equal lengths and canonical header sizes for every accepted binary-frame payload size. It clears MASK before using the server decoder; this is a length-header theorem, not a full masked-payload or whole-protocol equivalence theorem. |
| Payload masking | `CopyMasked`, `MaskSlice`: exact per-byte XOR, preserved source and bytes outside the destination slice, bounded indices and termination. | Sender supplies source/destination bounds. Copy-and-mask snapshots the key bytes; in-place masking proves the key region stays unchanged. Copying rejects source/destination aliasing. String encoding and RNG remain .NET dependencies. |
| Opening handshake | `HandshakeValid`: acceptance requires every supplied condition. | Request-specific key retention, SHA-1/Base64, HTTP version/header/token parsing, duplicate handling and subprotocol membership are C#/.NET boundaries, tested with parser and loopback cases. The boolean predicate does not prove those computations. |
| Closing payload | `CloseValid`, `CloseCode`: length, status-code ranges and supplied UTF-8-validity condition. | `ProtocolPayload` reads network-order status and uses a strict .NET UTF-8 decoder for reason text. Complete received text messages are validated after reassembly, so characters may span fragments. |

The synchronous contracts assume no concurrent mutation of supplied arrays. ArrayPool ownership, async writes, scheduling, cancellation, observer delivery, and teardown remain outside the proofs. Existing concurrency and teardown tests remain in place. `SendStep` proves legal outgoing opcode/state transitions, preserved pending state for controls, and prohibition of data sends after failure or Close. The C# data-message semaphore spans an entire list and the TCP write semaphore covers transition plus wire write; these ownership/scheduling guarantees are tested, not proved by Dafny. Manual continuation calls still require one application owner. The legacy single-byte empty-Pong compatibility option bypasses standard framing and has no RFC framing guarantee.

Receiving masked server frames is now rejected. Consequently the original `Masking.Apply`/`WebsocketMasking.Decode` pilot remains tested but is no longer used by the receive path; outgoing masking uses the verified slice kernels. Their contracts allow pooled buffers without allocating a temporary key or payload array.

## Protocol corrections

The implementation rejects malformed headers, nonminimal lengths, unsupported opcodes/RSV bits, orphan continuations and new data frames during an unfinished message. Aggregate size and fragmentation checks run before allocating or reading that frame's payload. Reassembly fixes Length and Fragment metadata, and validates UTF-8 only when a complete text message exists.

Handshake success is reported after validation against the actual request key. Invalid responses fail at the header terminator, including when a server keeps the connection open. The delegate captures headers directly because HttpMachine's completed response snapshot may not yet exist at that point. A missing selected subprotocol is allowed; an unoffered or multiple selection is rejected. Extensions are unsupported and rejected. Protocol-owned request headers cannot be overridden through custom headers.

Close validation rejects one-byte payloads, forbidden/unassigned status-code ranges, and invalid UTF-8 reasons. `CloseStep` additionally proves monotonic sent/received/terminal flags, at most one send decision, and a release decision only after both directions or timeout/failure. The sender consumes these decisions under its state lock; only the release decision completes its close waiter. Pong is permitted after a locally sent Close until the peer responds, and queued Pongs are suppressed after peer Close or terminal failure. Asynchronous disposal awaits the peer response or a bounded timeout after the Close write. Timer progress, lock ownership, write completion, close-code selection and the complete async handshake remain outside the proofs. Strict string encoding rejects invalid UTF-16 input; there is no new proof of incremental UTF-8 validity for manually supplied outgoing byte fragments.

Contracts follow [RFC 6455](https://www.rfc-editor.org/rfc/rfc6455.html), especially sections 4.1, 5.2–5.5, 7.4 and 8.1. Generated integration follows the [Dafny C# guide](https://dafny.org/dafny/DafnyRef/integration-cs/IntegrationCS).

## 10.1.0 state and composition proofs

- `Admit` joins header decoding and fragmentation admission. Rejection preserves the supplied state/total; an accepted data frame fits the remaining message budget.
- `SendStep` checks the actual wire opcode/FIN, including the continuation opcode produced by `Outgoing`. `SendFailed` is irreversible for data. A list is prevalidated and keeps the data-message gate across all fragments; controls use the independent frame gate. Failures after a completed fragment poison the message state; a possible partial write terminates all framing.
- `Merge` copies retained arrays into an exact-sized buffer with a quantified concatenation contract. `PartsPrefix` proves the prefix/length relationship used by the loops. Its single external leaf, `NativeCopy.CopySlice`, is implemented by `Buffer.BlockCopy` in `Helper/VerifiedNativeCopy.cs`: the proof assumes .NET copies the specified source bytes and preserves everything else. `MergeScalar` proves the complete per-byte reference algorithm without that external contract. Tests compare both with independent concatenation and check the native adapter's preservation behavior. The caller must establish that `total` equals the sum of segment lengths and that arrays cannot change during copying.

  This is an explicit performance/trust tradeoff. A local isolated benchmark found the verified scalar copy about 7–14 times slower than `Buffer.BlockCopy` for two-segment 1 KiB/64 KiB messages. Production retains bulk copying; the benchmark reports both implementations. No claim is made that .NET's bulk-copy implementation is verified by Dafny.
- `CloseStep` uses sent/received/terminal bits. The sent bit reserves the single Close attempt before the write; C# must await that write before releasing the transport. Receive and failure notifications wake the waiter. Synchronous disposal may abort at its separate bound, and is not covered by the graceful-completion guarantee.
- `HeaderRoundTrip` composes `Outgoing`, `WriteHeader`, `ReadLength` (through `DecodeHeader`) and the `NetworkWord` lemma. It covers lengths through `int.MaxValue - 14` without allocating payloads. Handshake parsing, cryptography and UTF-8 remain C#/.NET computations.

Regression tests exercise concurrent list/single sends, controls during lists, invalid later text items, cancellation after a first fragment, failed writes, manual sequencing, early header rejection without payload bytes, ordered merge, duplicate Close suppression and delayed peer Close. A passing test is evidence for these selected schedules, not a proof of every possible async interleaving.

## Reproduce verification

Run from the repository root in the Linux x64 dev container:

```bash
./scripts/dafny-masking.sh --check
# After changing either Dafny source:
./scripts/dafny-masking.sh --write
./scripts/dafny-masking.sh --check
```

The script retains its pilot name but verifies both modules, including included files. It downloads the checksum-pinned official Dafny 4.11.0 archive and uses its Z3 4.12.1 solver explicitly. Python 3, curl and unzip are required. Verification errors and assertion timeouts fail the command. The tool cache is outside the repository.

The generated file is checked in; ordinary builds and package consumers do not need Dafny or Z3. Never edit generated methods. Postprocessing only normalizes the machine-specific solver path in embedded metadata, marks generated partial classes internal, and adds generated-code/nullable headers. CI compares regenerated output byte-for-byte and uploads logs and artifacts. Dafny's compiler, solver, runtime and this limited postprocessing are trusted.

`DafnyRuntime` 4.11.0 is a new package dependency. Its netstandard2.0 DLL is approximately 172 KiB and declares System.Collections.Immutable and System.Runtime.Numerics dependencies. Generated support types require it even where hot loops use only native integer/byte operations. Runtime preconditions are not automatically enforced by compiled Dafny; adapters establish them explicitly or rely on documented .NET contracts such as array length and ArrayPool.Rent capacity.

There are no `assume` statements, `{:axiom}` attributes or verification-disabled implementations in the Dafny sources. There is one explicitly trusted external contract, `NativeCopy.CopySlice`, backed by the .NET `Buffer.BlockCopy` adapter. The generator audits both Dafny source roots and compares the findings with [trusted-boundaries.md](trusted-boundaries.md), so a newly introduced assumption or external contract fails the check until the report is explicitly reviewed and updated. It deliberately uses `--allow-external-contracts` for this boundary; this flag does not prove the adapter. All other algorithms retain verified bodies. Pure acceptance functions are executable specifications: their verification is not an independent proof that the RFC was interpreted correctly. Review the specification and adapters along with the proofs.

## Checks

```bash
dotnet build src/main/WebsocketClientLite.sln -c Release
./test-all-tfms.sh Release
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/protocol-coverage
./scripts/check-coverage.sh artifacts/protocol-coverage 70
DOTNET_TieredCompilation=0 dotnet run --project verification/benchmark/MaskingBenchmark.csproj -c Release
```

Use an empty coverage directory for each run because the existing coverage checker selects its first report. Generated source stays in the coverage report; handwritten adapters are not excluded. The test host is .NET 10 for every library TFM, including netstandard builds; this does not test every supported runtime/JIT.

Tests cover canonical length boundaries through 65536, integer limits without huge allocations, truncated headers, invalid masks/RSV/opcodes, control constraints, fragmentation transitions, message caps, UTF-8 split across fragments, Close payloads, request-specific accept validation, malformed negotiation and failure delivery with an open server connection. Byte-level tests compare with independent expected values, including the RFC Hello masking vector.

## Performance methodology

The benchmark compares the previous scalar loops with generated code using preallocated arrays, alternating order and medians of seven trials. It reports steady-state allocation counts separately. It also exercises fused outgoing copy-and-mask. Timing is not a CI gate because shared runner noise would make it unreliable.

The original pilot budget was zero per-call allocations, no more than 15% slowdown for payloads at least 125 bytes, and at most 20 ns added overhead below that size. Its whole-array loop met that budget on .NET 10.0.14. The sender experiment uses the same 15% budget for the measured payload sizes. These are local kernel measurements, not end-to-end network throughput or portable JIT guarantees.

## 10.1.0 local validation

- Pinned verifier: **41 verification results, zero errors**; regenerated C# matches the checked-in file. Counts include specification well-formedness, including the trusted primitive's contract, not independently proved RFC rules.
- Trust audit: exactly the two expected findings (preconditions/postconditions) for `NativeCopy.CopySlice`; both source roots are audited and the report comparison passes.
- Release solution build: **zero warnings/errors**.
- Target matrix: **184 tests passed per library target**, 920 executions, using a .NET 10 host.
- Coverage: **82.3% lines, 75.6% branches**, including generated code and the native adapter; the 70% line gate passes.
- Negative proof checks in temporary copies: unconditional Close release fails `CloseStep`'s postcondition; copying each segment at offset zero fails `Merge`'s concatenation assertion. Production sources were not mutated for these checks.

The benchmark command above reports the reassembly tradeoff as well as the existing masking kernels. Local .NET 10.0.14 medians for two-segment messages, including the destination allocation on every path:

| Payload bytes | Previous bulk copy ns | Verified orchestration + trusted bulk copy ns | Fully verified scalar reference ns |
| --- | ---: | ---: | ---: |
| 1024 | 42.58 | 43.30 | 330.72 |
| 65536 | 1149.09 | 1147.02 | 15806.92 |

These measurements support retaining the bulk-copy primitive in production; they are not portable timing guarantees. No package has been published and `GitVersion.yml` remains unchanged.

## Historical 10.0.0 local results

- Dafny: **23 verification results passed, zero errors**, both on regeneration and a subsequent clean generated-source check. These include specification well-formedness checks and are not a count of independently proved RFC requirements.
- Negative proof check: changing the first fused copy operation from XOR to OR fails its quantified output contract.
- Release solution build: **zero warnings/errors**.
- Target matrix: **161 tests passed per library target**, 805 executions across netstandard2.0, netstandard2.1, net8.0, net9.0 and net10.0.
- Coverage: **79.9% lines, 74.3% branches**; the existing 70% line gate passes with generated code included.
- Packages: both local packages built with temporary package version `0.0.0-protocol`; a separate consumer restored and compiled against them for all five targets without warnings/errors. No package was published and `GitVersion.yml` was not changed.

Final sender measurements on .NET 10.0.14 compare equivalent scalar loops with runtime offsets/counts, matching the sender's pooled-buffer layout. All measured paths allocate zero bytes in the measured regions.

| Kernel | Payload bytes | Previous scalar ns/call | Verified ns/call | Verified/previous |
| --- | ---: | ---: | ---: | ---: |
| Copy and mask | 125 | 51.54 | 45.15 | 0.876 |
| Copy and mask | 1024 | 409.87 | 365.58 | 0.892 |
| Copy and mask | 65536 | 25598.97 | 22983.36 | 0.898 |
| In-place slice | 125 | 54.65 | 51.58 | 0.944 |
| In-place slice | 1024 | 453.33 | 425.46 | 0.939 |
| In-place slice | 65536 | 28308.30 | 26539.79 | 0.938 |

The active sender kernels meet the local budget. Copy-and-mask uses four-byte batches plus a short tail; in-place masking reads the preserved key region directly. Neither uses a variable-shift runtime helper in its hot loop.

The retained, unused receive-side pilot helper measured approximately 45–50% slower than its original scalar comparison in this final build, despite meeting its earlier pilot measurements. It is no longer on the production receive path because masked server frames are rejected. The benchmark continues to report it; the original pilot result is not a portable performance guarantee. Do not reintroduce that helper into a hot path without remeasuring.
