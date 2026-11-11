# Experiment 2: named types at the boundary of the verified core

Written 2026-11-11 in step S7 of [the 10.3 session plan](../docs/pdd-10.3-session-plan.md)
(Appendix B, Experiment 2, items 1 and 3, before numbers only). This is the baseline.
The adapter, the after numbers, the per-entry result and the recommendation belong to
S8. No production, test, Dafny or generated file was changed in S7.

Base commit: `fa96d59` (`origin/releases/10.3.0.0` when S7 started, after #136 was
merged). Evidence: [`docs/evidence/core-boundary-types/baseline/`](../docs/evidence/core-boundary-types/baseline/).
Line numbers below are at the base commit.

## References verified

| Plan or brief says | Found at `fa96d59` | Difference |
| --- | --- | --- |
| DafnyRuntime paragraph at `verification/README.md` line 211 | Line 211. It says compiled Dafny does not enforce preconditions at run time; adapters establish them or rely on .NET contracts. | None |
| Benchmark command in "Checks" | Line 222: `DOTNET_TieredCompilation=0 dotnet run --project verification/benchmark/MaskingBenchmark.csproj -c Release` | None |
| Method in "Performance methodology" | Line 229: preallocated arrays, alternating order, medians of seven trials, allocations reported separately | None |
| Raw codes "byte status 0/1/2, byte state 0/1/2/8, close-state bits, event 0..2" | `DecodeHeader`/`Admit` status 0/1/2 (`Protocol.dfy:61`); `Step`/`Admit` state 0/1/2/8 (`Protocol.dfy:90`); `CloseStep` bits sent=1, received=2, terminal=4, state 0..7, event 0..2 (`Protocol.dfy:194-198`) | The brief omits two more codes: the transmit state of `SendStep`, 0/1/2/8/15 (`TxState`, `Protocol.dfy:159`), and the `Outgoing` fragment code 0/128/255 (`Protocol.dfy:424-425`). |
| CLOSE-LOCK says "events are 0..2"; REASSEMBLY-TOTALS and MASK-ADAPTER are "the caller passes a legal value" | All three entries exist in `verification/boundaries.json` with that wording | See section 2: only CLOSE-LOCK and HEADER-ADAPTER state a code range. REASSEMBLY-TOTALS and MASK-ADAPTER state relations between values and arrays. |

## 1. Call inventory

Found with the commands in `call-sites.txt` and `line-counts.txt`. Production calls the
generated core through `using Protocol = WebsocketClientLite.VerifiedProtocol.Protocol;`
in five files and `Verified.Masking` in one. There are **16 call expressions into 13
generated methods in 6 files**, plus one call in the other direction: the generated
`Merge` calls the hand-written `NativeCopy.CopySlice`.

**A precondition every call shares.** Dafny's `Size` and `Index` are `newtype`s over
0..0x7fffffff (`Protocol.dfy:2`, `Masking.dfy:3`), compiled to `uint`. A `uint` above
0x7fffffff is outside the proved domain and nothing checks it at run time. Every `Size`
argument below is a cast of a non-negative `int` (an array length, a UTF-8 byte count,
`MaxFrameSize`) or a value a generated method returned, so it holds by the .NET rule
that an `int` is at most `int.MaxValue`. The tables do not repeat this.

### Decision calls

The result is a code, a flag or a transition. None loops over the payload.

| ID | Call site | Generated method | Preconditions the caller must establish | Where it establishes them today | Runs |
| --- | --- | --- | --- | --- | --- |
| D1 | `Helper/DataframeParsing.cs:39` | `Admit`, prefix only (`available` 0) | `available <= extended.Length`; `state` in {0,1,2,8}; `total <= limit` | `Array.Empty<byte>()` with literal 0. `state` and `total` are parameters. Their only production caller, `WebsocketParserHandler.cs:28`, passes values that start at 0 (lines 20-21) and are only ever set from `Step`'s `next` and `size` (lines 38-39, 47-48), which `Step` ensures are in {0,1,2,8} and at most `limit`; a rejected `Step` throws at line 34 before anything is stored. `total` is reset to 0 at line 65. `limit` is `(uint)MaxFrameSize`, a get-only value in 1..`int.MaxValue` (`TcpConnectionService.cs:43`), so it is the same at every call. Nothing is checked at run time. `ReadDataframeAsync`'s defaults (`state = 0`, `total = 0`) also satisfy them. | Every received frame |
| D2 | `Helper/DataframeParsing.cs:46` | `Admit`, with the extended length | as D1, with `available` = `count` | Reached only on status 0, which `DecodeHeader` returns only when the length marker is 126 or 127, so `count` = `headerSize - 2` is 2 or 8. `ext` is `_headerScratch`, 8 bytes (`TcpConnectionService.cs:89`), filled with exactly `count` bytes or not returned (`TryFillAsync`, lines 115-140). `b0` and `b1` are copied to locals (lines 29-30) before this read reuses the same scratch array; the `late-scratch` seeded defect in `tools/Pdd/RetentionDefects.cs:28` breaks exactly that. | Frames with a 16-bit or 64-bit length |
| D3 | `Service/WebsocketParserHandler.cs:31` | `Step` | `state` in {0,1,2,8}; `total <= limit` | As D1: the loop owns `state` and `total` and stores only `Step`'s outputs. The loop stops when `state == 8` (line 41), so production never calls `Step` or `Admit` with state 8, although both accept it. `op` is `(byte)frame.Opcode` = `b0 & 0x0F` (`DataframeParsing.cs:36`); `Step` has no precondition on `op`. `length` is the payload length `Admit` accepted. | Every received frame |
| D4 | `Service/WebsocketSenderHandler.cs:44` in `AdvanceClose(byte action)` | `CloseStep` | `state <= 7`; `event <= 2`; callers serialized | `_closeState` starts at 0 (field default, line 37) and is written only through `CloseStep`'s `out _closeState`, which ensures `next <= 7`. `AdvanceClose` accepts any byte. Its four callers pass literals: 0 at line 422 (`WriteFrameSerializedAsync`), 1 at line 51 (`NotifyPeerClose`), 2 at line 59 (`NotifyReadFailure`), 2 at line 69 (`WaitForPeerCloseAsync`, on timeout). Every caller holds `_stateLock` (lines 51, 56, 69, 418). | Each Close frame sent, Close received, failure or timeout |
| D5 | `Service/WebsocketSenderHandler.cs:429` | `SendStep` | `TxState(state)`: state in {0,1,2,8,15} | `_sendState` starts at 0 (line 36) and is written only from `SendStep`'s `next` (line 431, ensured `TxState`) and from `SendFailed` (lines 58, 308), which returns 8 or 15. Held under `_stateLock` (line 418). `op` = `buffer[0] & 15` (line 417) and `fin` = `buffer[0] & 128`; no precondition on them. | Every frame written |
| D6 | `Service/WebsocketSenderHandler.cs:58` | `SendFailed` | none (a function) | Under `_stateLock` (line 56). | Read or write failure |
| D7 | `Service/WebsocketSenderHandler.cs:308` | `SendFailed` | none | Under `_stateLock`, in `SendList`'s catch after a fragment was sent. | Failed list send |
| D8 | `Service/WebsocketSenderHandler.cs:286` | `Outgoing`, list prevalidation | none beyond `Size` | `(byte)opcode` is `OpcodeKind.Text` or `OpcodeKind.Binary` from the two callers (lines 141, 174); fragment is the literal 255. Only `valid` is used. | Each list item, before any write |
| D9 | `Service/WebsocketSenderHandler.cs:356` | `Outgoing` | none beyond `Size` | Line 354 rejects `(uint)opcode > 15` and `(uint)fragment > 255` before the `(byte)` casts, so the casts do not wrap. `Outgoing` rejects every other illegal combination itself (`ok` is false). Its `first`, `header` and `total` size the rented buffer and feed E1. | Every frame composed |
| D10 | `Helper/ProtocolPayload.cs:30` | `CloseValid` | none (a function) | `code` is read big-endian at line 23; `utf8` comes from the strict decoder at lines 25-29. Called from `DataframeParsing.cs:68` (Close received) and `WebsocketSenderHandler.cs:362` (Close sent). | Each Close frame |
| D11 | `Parser/HandshakeParser.cs:33` | `HandshakeValid` | none (a function of six booleans) | The C# computes each boolean at lines 29-36: status 101 with HTTP/1.1, the `Upgrade` and `Connection` tokens, the accept value against the request key, the subprotocol selection and no extensions. | Once per connection |

The hand-written code also reads raw codes outside the calls: `status == 0` and
`status != 2` (`DataframeParsing.cs:41`, 49), `(byte)frame.Opcode >= 8`, `state == 0`
and `state == 8` (`WebsocketParserHandler.cs:36`, 45, 41), and the close bits
`(_closeState & 6) != 0` and `_closeState != 0` (`WebsocketSenderHandler.cs:426`, 427).
An adapter that types only the call arguments leaves these as numbers.

Observation, not changed here: `Admit` already runs `Step` on the same state, opcode,
FIN, total and limit (`Protocol.dfy:150`). `DataframeParsing` discards `Admit`'s
`next`, `size` and `emit` (`out _`), and `WebsocketParserHandler` calls `Step` again
(D3) to get them.

### Header encoding

Fixed-size byte writes per frame, neither a decision nor a payload loop.

| ID | Call site | Generated method | Preconditions | Where established | Runs |
| --- | --- | --- | --- | --- | --- |
| E1 | `Service/WebsocketSenderHandler.cs:368` | `WriteHeader` | `header == (6, 8 or 14 by length)`; `buffer.Length >= header` | `header` is `Outgoing`'s output for the same `payloadLength`, used only after `valid` is checked (line 358); `Outgoing` ensures that equation when `ok`. `buffer` is `_framePool.Rent(total)` (line 364) with `total = header + length`, and `ArrayPool.Rent` returns at least the requested length (a .NET contract). | Every frame composed |

### Hot byte loops

The work grows with the payload.

| ID | Call site | Generated method | Preconditions | Where established | Runs |
| --- | --- | --- | --- | --- | --- |
| H1 | `Service/WebsocketSenderHandler.cs:390` | `CopyMasked` | `source != dest`; `count <= source.Length`; `offset + count <= dest.Length`; `offset + count <= 0x7fffffff` | `ReferenceEquals(content, buffer)` throws at line 388. `count` = `payloadLength` = `content.Length` (line 353). `offset` = `header` (lines 369-381) and `dest` is rented for `header + length`. `Outgoing` returned `ok` only if that total is at most 0x7fffffff. The key is passed as four byte values, a snapshot. | Binary frames with a payload |
| H2 | `Service/WebsocketSenderHandler.cs:398` | `MaskSlice` | `keyOffset + 4 <= offset`; `offset + count <= dest.Length`; `offset + count <= 0x7fffffff` | `maskOffset` = `header - 4`, `payloadOffset` = `header`. Bounds as H1. The text is encoded at line 397 with the same strict encoder that counted `payloadLength` at line 353, so it writes exactly `count` bytes (a .NET contract; the string is immutable). | Text frames with a payload |
| H3 | `Service/WebsocketParserHandler.cs:58` | `Merge` | `parts.Length <= 0x7fffffff`; the summed length of `parts` equals `total` | `segments` is a `List<byte[]>`, so its count is an `int`. `total` is `Step`'s `size`, the sum of the accepted data lengths since the message started, and `segments` holds every non-empty payload of that message (line 46); empty payloads add 0. That equality is C# bookkeeping, not proved (REASSEMBLY-TOTALS). Payload arrays are fresh per frame (`TcpConnectionService.cs:98`) and not published before the message is emitted. Called only with two or more segments (lines 52-53). | End of each fragmented message |
| H4 | `Helper/WebsocketMasking.cs:26` | `Masking.Apply` | `data != key`; `key.Length >= 4`; `count == data.Length` | Null checks (lines 15-21), key length (line 22) and aliasing (line 23) throw before the call; `count` is `data.Length`. | No production caller: masked server frames are rejected, so the receive path no longer unmasks (`verification/README.md:172`). `MaskingTests` and the benchmark call it. |

### The other direction

| ID | Site | Contract | Who establishes it |
| --- | --- | --- | --- |
| X1 | `Helper/VerifiedNativeCopy.cs:10`, `NativeCopy.CopySlice`, called by the generated `Merge` | `{:extern}` with `requires source != dest` and `offset + source.Length <= dest.Length` (`Protocol.dfy:566-571`) | Dafny proves that `Merge` passes legal arguments. The hand-written body, `Buffer.BlockCopy` with `checked((int)offset)`, is the trusted part (NATIVE-COPY). |

### Generated methods production does not call directly

`DecodeHeader` (inside `Admit`; tests call it), `ReadLength`, `Known`, `PrefixValid`,
`HeaderValid`, `TxState` and `CloseCode` (inside other methods), `MergeScalar`
(tests and benchmark) and `HeaderRoundTrip` (one test). The support types `Size._Is`,
`Index._Is`, `Dafny.ArrayHelpers` and `FuncExtensions` have no hand-written caller.

Hand-written callers outside production, counted in `line-counts.txt`:
`ProtocolCoreTests` (9 calls), `ProtocolStateTests` (13, including 7 `CloseStep`),
`PayloadBoundaryTests` (4), `MaskingTests` (through `WebsocketMasking.Decode`) and the
benchmark (`Decode`, `CopyMasked`, `MaskSlice`, `Merge`, `MergeScalar`).

### How preconditions are established today, by kind

- **Checked at run time before the call:** H4 (null, key length, aliasing), H1
  (aliasing), D9 (opcode and fragment range before the cast), and `Outgoing`'s `valid`
  before E1, H1 and H2 run.
- **Data flow from an earlier verified result:** the stored codes are only ever written
  from a generated method's ensured output. This covers the receive state and total
  (D1-D3), the close state (D4) and the transmit state (D5). No check at run time.
- **Literals at the call site:** the close events (D4), `available` 0 (D1), fragment
  255 (D8).
- **.NET contracts:** `int` range (every `Size`), `ArrayPool.Rent` capacity (E1, H1,
  H2), strict UTF-8 byte counts (H2), `Buffer.BlockCopy` (X1).
- **Unproved C# bookkeeping:** the `Merge` total (H3).
- **Total functions:** `Outgoing`, `SendFailed`, `CloseValid` and `HandshakeValid` have
  no `requires`. An illegal input gives a defined result, usually a rejection.

The code domains a named type could restrict are therefore the receive state
{0,1,2,8}, the transmit state {0,1,2,8,15}, the close state 0..7, the close event 0..2,
and as outputs the `Admit` status 0/1/2. Opcode and fragment have no precondition in
the core. This is a description for S8, not a design.

## 2. `boundaries.json` entries and the calls they cover

The mapping goes through each entry's `owners`: a call is covered when it is in an
owner symbol or in a method an owner calls directly. "Value part" is the part of the
assumption that says which values the caller passes. A named type could only ever
address that part.

| Entry | Calls covered | Value part of the assumption | Rest of the assumption |
| --- | --- | --- | --- |
| CLOSE-LOCK | D4 at all four event sites (owners `AdvanceClose`, `WriteFrameSerializedAsync`, `NotifyPeerClose`, `NotifyReadFailure`, `WaitForPeerCloseAsync`). D5 and D6 sit in owner symbols, but the assumption text names only `CloseStep`. | "initial state is zero and events are 0..2" | All calls serialize under `_stateLock` |
| CLOSE-WRITE | D4 event 0 (line 422) and D5, through `WriteFrameSerializedAsync`. `TcpConnectionService.WriteSerializedAsync`, `DisconnectWebsocket` and `DisposeAsync` contain no core call; `DisconnectWebsocket` reaches D9, E1, H1, D10, D4 and D5 through `SendCloseHandshakeAsync` (`WebsocketConnectionHandler.cs:289`). | None | Reservation is not write completion; the write gate and the write delegate |
| CLOSE-FAILURE | D6 and D4 event 2 (line 59) through `NotifyReadFailure`; D4 and D5 in `WriteFrameSerializedAsync`, whose catch calls `NotifyReadFailure` (line 438). `DisconnectWebsocket` as in CLOSE-WRITE. | None | A failed or partial write is never retried as a fresh Close |
| CLOSE-TIMER | D4 event 2 at line 69, through `WaitForPeerCloseAsync`. `DisconnectWebsocket` as in CLOSE-WRITE. | None | Timers and continuations eventually run |
| CLOSE-OBSERVER | No direct core call. `ObserveFrames` reaches D6 and D4 through `NotifyReadFailure` (`WebsocketConnectionHandler.cs:255`); `DisconnectWebsocket` as in CLOSE-WRITE. | None | Callbacks and disposal return; no concurrent teardown |
| PAYLOAD-OWNERSHIP | H1, H2, E1, D9 and D10 in `ComposeFrameAndSendCoreAsync`; D3 and H3 in `DataframeObservable`; H4 in `WebsocketMasking.Decode`; X1. The test owner `SmallSequencesPreserveBytesMetadataAndReset` calls no core method directly. | Source and destination are not aliased (checked for H1 at line 388 and for H4 at line 23) | Arrays stay stable; pool and write-delegate ownership |
| REASSEMBLY-TOTALS | H3 (`total`) and D3 (which computes it) in `DataframeObservable`. The two test owners exercise `Merge` and the parser. | "the exact retained-byte total within Size bounds": an equation between `total` and the segment list | Empty segments do not change the sum |
| NATIVE-COPY | X1, and H3 in `DataframeObservable` | "distinct arrays and valid bounds", which Dafny proves `Merge` passes | `Buffer.BlockCopy` implements the contract |
| MASK-ADAPTER | H4 in `Decode`; H1, H2 and E1 in `ComposeFrameAndSendCoreAsync` | Null, alias, key length, slice bounds, payload count and header key: relations between offsets, counts and arrays | Buffer ownership |
| WRITE-PROGRESS | None. `WriteSerializedAsync` runs the delegate that contains D4 and D5 but calls no core method itself. | None | Scheduling and settling |
| EPOCH-PROGRESS | None. `ClientWebSocketRx.DisposeAsync` calls no core method. | None | Connect, handshake and I/O settle; Rx disposal |
| HEADER-ADAPTER | D1 and D2 in `ReadDataframeAsync`; D3 in `DataframeObservable` | "the parser's message state and retained total" (state in {0,1,2,8}, `total <= limit`); "exactly the extended-length bytes read after them with their count" | Reads more header bytes only on status 0; reads or allocates the payload only on status 2 |

Calls that no entry owns:

- D7, `SendFailed` in `SendList` (line 308), and D8, `Outgoing` in `SendList`
  (line 286): `SendList` is not an owner symbol.
- D10, `CloseValid`, and D11, `HandshakeValid`: `ProtocolPayload.ValidateClose` and
  `HandshakeParser.Parse` are not owner symbols. D10 is reached through two owners
  (`ReadDataframeAsync`, `ComposeFrameAndSendCoreAsync`) whose assumptions do not
  mention the Close payload.
- D5's precondition `TxState(state)`: D5 sits in two owner symbols, but no entry's
  assumption states the transmit-state range, as CLOSE-LOCK does for the close state.

## 3. Baseline numbers

Local measurements on one machine, not CI and not portable: AMD Ryzen 9 9955HX
(32 logical cores), Linux 7.2.11, .NET SDK 10.0.403, runtime .NET 10.0.14,
`DOTNET_TieredCompilation=0`, Release (`environment.txt`). The benchmark was run three
times in a row, each under the shared timing lock `flock ~/worktrees/.timing.lock`.
The `ps` output and the load average taken inside the lock just before each run are in
`benchmark-run-N.meta`. The load average was 0.58 to 0.73, and the only other
`claude -p` process was a code review session. Every run is reported; none was
repeated or left out.

The benchmark covers only the hot byte loops: H4 (`Masking.Apply` through `Decode`),
H1, H2, H3 and `MergeScalar`. It calls no decision method and no `WriteHeader`.

Medians of seven trials, nanoseconds per call, runs 1 / 2 / 3. Every masking row
allocated 0 bytes in the measured region in every run. The merge rows do not report
allocations.

| Kernel | Bytes | Previous scalar ns | Verified ns | Verified / previous |
| --- | ---: | --- | --- | --- |
| `Masking.Apply` | 0 | 1.78 / 1.79 / 1.78 | 2.61 / 2.58 / 2.58 | 1.463 / 1.447 / 1.445 |
| `Masking.Apply` | 4 | 2.39 / 2.38 / 2.38 | 3.29 / 3.21 / 3.25 | 1.377 / 1.345 / 1.370 |
| `Masking.Apply` | 125 | 34.82 / 34.40 / 34.05 | 35.43 / 35.86 / 35.01 | 1.018 / 1.042 / 1.028 |
| `Masking.Apply` | 1024 | 280.54 / 279.92 / 278.79 | 284.32 / 282.48 / 281.66 | 1.013 / 1.009 / 1.010 |
| `Masking.Apply` | 65536 | 17391.55 / 17371.27 / 17353.43 | 17402.09 / 17385.86 / 17345.90 | 1.001 / 1.001 / 1.000 |
| `Masking.Apply` | 1048576 | 277700.99 / 277841.18 / 277689.02 | 277896.28 / 277893.31 / 277662.95 | 1.001 / 1.000 / 1.000 |
| `CopyMasked` | 125 | 51.22 / 51.25 / 51.21 | 44.16 / 44.15 / 44.24 | 0.862 / 0.861 / 0.864 |
| `CopyMasked` | 1024 | 415.11 / 415.80 / 414.76 | 369.07 / 372.59 / 366.08 | 0.889 / 0.896 / 0.883 |
| `CopyMasked` | 65536 | 26023.96 / 26009.64 / 26000.15 | 22994.08 / 22973.08 / 22956.65 | 0.884 / 0.883 / 0.883 |
| `MaskSlice` | 125 | 54.71 / 62.10 / 62.48 | 51.62 / 51.70 / 51.60 | 0.944 / 0.833 / 0.826 |
| `MaskSlice` | 1024 | 450.71 / 456.39 / 450.40 | 427.83 / 425.46 / 428.88 | 0.949 / 0.932 / 0.952 |
| `MaskSlice` | 65536 | 28311.77 / 28324.06 / 28290.20 | 26533.27 / 26551.39 / 26532.98 | 0.937 / 0.937 / 0.938 |

| Reassembly, two segments | Bytes | `Buffer.BlockCopy` ns | Verified `Merge` ns | Verified `MergeScalar` ns |
| --- | ---: | --- | --- | --- |
| H3 | 1024 | 38.47 / 38.25 / 38.64 | 40.17 / 40.21 / 39.93 | 306.16 / 303.46 / 299.09 |
| H3 | 65536 | 1082.75 / 1068.35 / 1071.88 | 1071.55 / 1077.77 / 1068.76 | 14836.42 / 14892.28 / 14835.32 |

The run-to-run spread of a verified median is at most 2.5% (`Masking.Apply`,
4 bytes), then 2.4% (`Masking.Apply`, 125 bytes, and `MergeScalar`, 1024 bytes). The previous-scalar
`MaskSlice` median at 125 bytes moved by 14% between run 1 and runs 2 and 3, so its
ratio moved from 0.944 to 0.826. S8 should compare verified medians run by run, not
ratios alone.

### Line counts

From `line-counts.txt`. "Code" counts lines that are neither blank nor only a `//`
comment.

| Scope | Total lines | Code lines |
| --- | ---: | ---: |
| `Helper/DataframeParsing.cs` (D1, D2) | 90 | 75 |
| `Service/WebsocketParserHandler.cs` (D3, H3) | 73 | 65 |
| `Service/WebsocketSenderHandler.cs` (D4-D9, E1, H1, H2) | 492 | 417 |
| `Helper/ProtocolPayload.cs` (D10) | 33 | 29 |
| `Parser/HandshakeParser.cs` (D11) | 44 | 38 |
| `Helper/WebsocketMasking.cs` (H4) | 43 | 33 |
| `Helper/VerifiedNativeCopy.cs` (X1) | 12 | 7 |
| The six caller files together | 775 | 657 |
| `src/main/WebsocketClientLite`, 24 `.cs` files | 2733 | 1992 |
| `src/main/WebsocketClientLiteTest`, 23 `.cs` files | 3906 | 3358 |
| `verification/generated/Masking.g.cs` | 1129 | 1076 |
| `verification/Protocol.dfy` / `Masking.dfy` | 574 / 42 | 433 / 38 |
| Production call expressions into the core | 16 | |

### Checks in S7

| Check | Result |
| --- | --- |
| Benchmark build (`dotnet build verification/benchmark/MaskingBenchmark.csproj -c Release`) | run and passed |
| Benchmark, three runs under the timing lock | run and passed (all three completed; run 1 has no wall time, see `commands.json`) |
| Release solution build | run and passed, 0 warnings, 0 errors |
| `./scripts/pdd.sh validate` | run and passed, 16 requirements |
| Five-target matrix, coverage gate, Pdd tests, `dafny-masking.sh --check`, mutation suite, capture jobs | not run: S7 changes no code, and its brief asks for the before numbers only |

## Decisions for owner review

1. **Three benchmark runs, not one.** The README describes one run. S7 ran the same
   command three times so that S8 has a spread to compare its after numbers against
   (lesson 3: one run is not enough). Every run is kept and reported.
2. **Where the report lives.** This file, in the place S5 chose for Experiment 1's
   `verification/reconnect-decision.md` (on its own branch, not yet on the release
   branch). S8 adds its results here.
3. **The benchmark cannot show that an adapter for decision calls is free.** It runs
   only the byte kernels, and the benchmark project compiles only
   `VerifiedNativeCopy.cs`, `Masking.g.cs` and `WebsocketMasking.cs`. An adapter around
   D1-D11 will leave these numbers unchanged by construction. The per-frame decision
   calls (D1-D3, D5, D9) and E1 have no benchmark. The brief asks for the benchmarks
   "to show the hot path is unchanged". They will show that for the byte loops only.
   Whether S8 should add a per-frame decision measurement is the owner's call: it is
   new tooling, which the ground rules say to ask about first.
4. **The brief's examples are partly not code ranges.** Of the three examples, only
   CLOSE-LOCK ("events are 0..2") states a range of codes. REASSEMBLY-TOTALS states that
   `total` equals the sum of the segment lengths, and MASK-ADAPTER states bounds and
   aliasing between offsets, counts and arrays. Enums or small structs over codes cannot
   express either of those. HEADER-ADAPTER, not named in the brief, also states a code
   range (the message state). This is a prediction for S8 (lesson 9), not a result.
5. **Gaps in `boundaries.json`, reported, not edited.** D7, D8, D10 and D11 are owned
   by no entry, and no entry states D5's `TxState` precondition. Whether these need
   entries is outside S7.
6. **The double transition (observation).** `Admit` computes the same `Step` that
   `WebsocketParserHandler` then computes again. Nothing was changed. It matters to S8
   only if an adapter would wrap both calls.
