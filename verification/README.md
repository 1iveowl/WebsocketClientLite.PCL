# Verified WebSocket protocol core

The library calls C# generated from Dafny for synchronous protocol decisions and byte operations. `Masking.dfy` contains the original whole-array masking pilot and includes `Protocol.dfy`, which implements the remaining core. `generated/Masking.g.cs` contains both modules. Generated types are internal.

This work implements the five stages in the [adoption plan](../docs/dafny-pdd-plan.md): header validation, fragmentation, outgoing framing/masking, handshake/Close predicates, and integration checks. It does not establish full RFC conformance or prove the asynchronous client.

## Contracts used by production

| Area | Dafny symbol and guarantee | C# caller and remaining boundary |
| --- | --- | --- |
| Header parsing | `DecodeHeader`: NeedMore/Reject/Accept; never reads beyond available extended bytes; acceptance requires valid server framing and bounded length. `ReadLength` equals a big-endian byte fold. | `DataframeParsing.ReadDataframeAsync` owns stream reads, cancellation and payload allocation. It reads only the requested extended bytes before validation, and allocates only after acceptance. |
| Fragmentation | `Step`: exact legal state/opcode transitions; rejection preserves state; accepted accumulated sizes remain bounded; controls preserve pending state except Close; completion requires FIN. | `WebsocketParserHandler` applies each transition, retains segments in order, concatenates with `Buffer.BlockCopy`, fixes message metadata, and emits messages. This C# loop and concatenation are tested/reviewed, not proved. |
| Outgoing frame sizing | `Outgoing`: canonical header size, bounded total size, defined FIN/opcode mapping and control-frame constraints. | `WebsocketSenderHandler` validates enum ranges before conversion, rents the buffer and uses the returned sizes. |
| Header encoding | `WriteHeader`: exact marker and network-order length bytes; key/payload region unchanged. | Sender supplies the validated sizes. Round-trip encoding/decoding is independently tested; there is no combined encoder/decoder equivalence theorem. |
| Payload masking | `CopyMasked`, `MaskSlice`: exact per-byte XOR, preserved source and bytes outside the destination slice, bounded indices and termination. | Sender supplies source/destination bounds. Copy-and-mask snapshots the key bytes; in-place masking proves the key region stays unchanged. Copying rejects source/destination aliasing. String encoding and RNG remain .NET dependencies. |
| Opening handshake | `HandshakeValid`: acceptance requires every supplied condition. | Request-specific key retention, SHA-1/Base64, HTTP version/header/token parsing, duplicate handling and subprotocol membership are C#/.NET boundaries, tested with parser and loopback cases. The boolean predicate does not prove those computations. |
| Closing payload | `CloseValid`, `CloseCode`: length, status-code ranges and supplied UTF-8-validity condition. | `ProtocolPayload` reads network-order status and uses a strict .NET UTF-8 decoder for reason text. Complete received text messages are validated after reassembly, so characters may span fragments. |

The synchronous contracts assume no concurrent mutation of supplied arrays. ArrayPool ownership, async writes, scheduling, cancellation, observer delivery, and teardown remain outside the proofs. Existing concurrency and teardown tests remain in place. Frame serialization is not a proof that independently initiated fragmented sends cannot interleave. The legacy single-byte empty-Pong compatibility option bypasses standard framing and has no RFC framing guarantee.

Receiving masked server frames is now rejected. Consequently the original `Masking.Apply`/`WebsocketMasking.Decode` pilot remains tested but is no longer used by the receive path; outgoing masking uses the verified slice kernels. Their contracts allow pooled buffers without allocating a temporary key or payload array.

## Protocol corrections

The implementation rejects malformed headers, nonminimal lengths, unsupported opcodes/RSV bits, orphan continuations and new data frames during an unfinished message. Aggregate size checks run before storing a segment. Reassembly fixes Length and Fragment metadata, and validates UTF-8 only when a complete text message exists.

Handshake success is reported after validation against the actual request key. Invalid responses fail at the header terminator, including when a server keeps the connection open. The delegate captures headers directly because HttpMachine's completed response snapshot may not yet exist at that point. A missing selected subprotocol is allowed; an unoffered or multiple selection is rejected. Extensions are unsupported and rejected. Protocol-owned request headers cannot be overridden through custom headers.

Close validation rejects one-byte payloads, forbidden/unassigned status-code ranges, and invalid UTF-8 reasons. This does not prove or redesign close-code selection, echoing, timers or the async closing handshake. Strict string encoding rejects invalid UTF-16 input; there is no new proof of incremental UTF-8 validity for manually supplied outgoing byte fragments.

Contracts follow [RFC 6455](https://www.rfc-editor.org/rfc/rfc6455.html), especially sections 4.1, 5.2–5.5, 7.4 and 8.1. Generated integration follows the [Dafny C# guide](https://dafny.org/dafny/DafnyRef/integration-cs/IntegrationCS).

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

There are no unchecked assumptions, external algorithm implementations, or verification-disabled methods in the Dafny sources. Pure acceptance functions are executable specifications: their verification is not an independent proof that the RFC was interpreted correctly. Review the specification and adapters along with the proofs.

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

## Final local results

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
