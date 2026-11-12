# Experiment 2: named types at the boundary of the verified core

Written 2026-11-11 in step S7 of [the 10.3 session plan](../docs/pdd-10.3-session-plan.md)
(Appendix B, Experiment 2, items 1 and 3, before numbers only). Sections 1 to 3 are
the baseline; no production, test, Dafny or generated file was changed in S7. Step S8
added sections 4 to 8 on the same day: the adapter, the after numbers, the per-entry
result and the recommendation (adopt).

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
`claude -p` process was a code review session. (On 2026-11-11 the process lists were
reduced to process names, because they exposed other sessions' prompts, so this can no
longer be checked from the `.meta` files; see the evidence README.) Every run is
reported; none was repeated or left out.

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

## Decisions for owner review (S7)

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

## 4. The adapter (S8)

Written 2026-11-11 in step S8 (Appendix B, Experiment 2, items 2, 3 after and 4).
Production commit: `13ed44f`, on top of S7's `d5c648d`. It holds the production change
and its plain tests only, so that it can be reverted on its own. The Dafny sources and
the generated file are unchanged. Evidence:
[`docs/evidence/core-boundary-types/after/`](../docs/evidence/core-boundary-types/after/)
and [`decision-path/`](../docs/evidence/core-boundary-types/decision-path/).

### What it is

`src/main/WebsocketClientLite/Helper/CoreBoundaryTypes.cs` adds four internal
`readonly struct`s. Each holds one `readonly byte`, has one private constructor and no
conversion into the type. (Changed at M4, section 9: no type has a conversion out of it
either, each exposes its code only as `Code`, and `default(CloseEvent)` is no event.)

| Type | Codes | Values come only from | Wrapper that forwards to the generated method |
| --- | --- | --- | --- |
| `ReceiveState` | 0, 1, 2, 8 | `Idle` (= `default`), `FromCode` (throws for any other byte), `Step`'s ensured `next` | `ReceiveState.Step` → `Protocol.Step`. In S8 the two `Protocol.Admit` calls took a `ReceiveState` through an implicit conversion to `byte`; since M4 `ReadDataframeAsync` takes `.Code` once into a local `byte state` (section 9). |
| `TransmitState` | 0, 1, 2, 8, 15 (`TxState`) | `Idle` (= `default`), `SendStep`'s ensured `next`, `SendFailed`'s result (8 or 15) | `TransmitState.SendStep`, `TransmitState.SendFailed` |
| `CloseState` | 0..7 | `Open` (= `default`), `CloseStep`'s ensured `next` | `CloseState.Step` → `Protocol.CloseStep` |
| `CloseEvent` | 0, 1, 2 | `Send` (= `default` in S8; since M4 `default` is no event), `Received`, `TimeoutOrFailure` | (argument of `CloseState.Step`) |

The call sites after the change, by S7's IDs:

| ID | Before | After |
| --- | --- | --- |
| D1, D2 | `Protocol.Admit(..., state, total, ...)` with `byte state` | Same text. `state` is a `ReceiveState` parameter of `DataframeParsing.ReadDataframeAsync(ReceiveState, uint, CancellationToken)`. |
| D3 | `Protocol.Step(state, ...)`, `byte state` | `ReceiveState.Step(state, ..., out ReceiveState next, ...)`; `state == 8` and `state == 0` become `state.IsClosed` and `state.IsIdle` |
| D4 | `AdvanceClose(byte action)`, callers pass 0, 1, 2, 2 | `AdvanceClose(CloseEvent action)`, callers pass `CloseEvent.Send`, `Received`, `TimeoutOrFailure`, `TimeoutOrFailure`; `_closeState` is a `CloseState`; `(_closeState & 6) != 0` and `_closeState != 0` become `ReceivedOrTerminal` and `Started` |
| D5 | `Protocol.SendStep(_sendState, ...)`, `byte _sendState` | `TransmitState.SendStep(...)`, `_sendState` is a `TransmitState` |
| D6, D7 | `Protocol.SendFailed(_sendState)` | `TransmitState.SendFailed(_sendState)` |
| D8, D9, D10, D11 | `Outgoing`, `CloseValid`, `HandshakeValid` | Unchanged: these have no `requires`, so there is no illegal call to make unrepresentable. A type over their opcode, fragment or boolean inputs would only rename them. |
| E1, H1-H4, X1 | header encoding and byte loops | Unchanged, as the brief says |

Tests call `ReadDataframeAsync(ct, 2, retained)` and `ReadDataframeAsync(ct)` with raw
codes (`ParserRetentionTests`, `PayloadParsingTests`) and must stay unchanged. In S8 that
entry point was kept in the production assembly, in its own class
`DataframeParsingRawState`, so that `DataframeParsing.ReadDataframeAsync`, which
`requirements.json` and `boundaries.json` name, stays one declaration (section 8,
decision 6). Since M4 the class is in the test project
(`src/main/WebsocketClientLiteTest/TestConnections.cs`), so production has no entry point
that takes a raw state. It converts with `ReceiveState.FromCode` and rejects a total
above the connection's limit, and both failures fault the returned task before any byte
is read (section 9).

### Can a caller construct an out-of-range state or event? (Done when 1)

No, in safe C#, for code outside the four types: the constructor is private and there is
no conversion into a type, so the C# compiler rejects any other way to make a value
except `default`. Code inside a type can call its private constructor with any byte;
since M4 a plain test pins which members do (section 9, finding 8). The evidence is in
the plain tests in `src/main/WebsocketClientLiteTest/CoreBoundaryTypesTests.cs` (the S8
list follows; section 9 lists the M4 additions):

- `OnlyPrivateConstructorsAndOneByteField`: each type is a readonly struct with one
  private constructor, one readonly `byte` field and no conversion into the type.
- `DefaultIsCodeZeroAndNamedZeroInstances`: `default(T)` is code 0, which is legal for
  each of the four ranges.
- `FromCodeAcceptsExactlyTheReceiveStates`: all 256 bytes; exactly 0, 1, 2 and 8 convert.
- `ReceiveStepMatchesStepAndReachesExactlyTheReceiveStates`,
  `TransmitStepMatchesSendStepAndReachesExactlyTxState`,
  `CloseStepMatchesCloseStepAndStaysWithinSevenBits`: from the zero value, every
  wrapper is applied to every reachable state with every opcode, FIN flag and event
  (and several totals and lengths for `Step`). Every result equals the generated
  method's, and the reachable codes are exactly {0, 1, 2, 8}, exactly `TxState`'s
  {0, 1, 2, 8, 15}, and within 0..7.
- `RawReadRejectsAnIllegalStateBeforeReadingAnyByte`: state 3 through the raw entry
  throws and the stream position stays 0; state 0 reads the frame.
- `WrappersDoNotAllocate`: 10,000 rounds of every wrapper allocate 0 bytes on the thread.

What the types do not stop, stated exactly:

- **Outside safe C#.** Reflection (`FieldInfo.SetValue` on a boxed value),
  `Unsafe.As` or unsafe code can write any byte into the field. The library has no
  unsafe code and no `AllowUnsafeBlocks`; its tests use reflection only to set
  `TcpConnectionService._stream`.
- **`default(CloseEvent)` is `Send`.** It is a legal event, so a forgotten argument
  cannot become illegal, but it is the event that requests a Close. (Fixed at M4:
  `default(CloseEvent)` is no event and its `Code` throws; section 9.)
- **Relations, not ranges.** `total <= limit` (D1-D3), `available <= extended.Length`
  (D2), the `Merge` total (H3) and every `Size` range stay as they were: the type
  holds a code, not a relation between two values. See decision 4 for a cursor that
  would cover `total <= limit`.
- **The raw entry point.** `DataframeParsingRawState.ReadDataframeAsync` takes a byte
  and checks it at run time. That is a check, not a type. (Since M4 it is in the test
  project and also checks the total; section 9.)
- **Members inside a type.** Any member of a type can call its private constructor with
  an arbitrary byte; `OnlyPrivateConstructorsAndOneByteField` does not show otherwise.
  (Since M4 `OnlyTheListedMembersCallThePrivateConstructor` pins the members that do;
  section 9.)

### Seeded defects in the adapter

`after/seeded/seed.sh` copies `13ed44f` with `git archive`, changes one line of
`CoreBoundaryTypes.cs` and runs only `CoreBoundaryTypesTests` (Release, net10.0). Each
`.meta` has the source and mutant hashes.

| Defect | Edit | Result |
| --- | --- | --- |
| none (control) | no change | 11 of 11 passed |
| `fromcode-accepts-3` | `FromCode` also accepts 3 | 2 failed: `FromCodeAccepts...`, `RawReadRejects...` |
| `received-is-event-2` | `CloseEvent.Received` is code 2 | 2 failed: `DefaultIsCodeZero...`, `WrappersDoNotAllocate` (its value check) |
| `sendstep-keeps-state` | `SendStep` returns the old state | 1 failed: `TransmitStepMatches...` |
| `step-drops-state` | `Step` passes state 0 to the core | 2 failed: `ReceiveStepMatches...`, `WrappersDoNotAllocate` (its value check) |

The plain tests alone catch each one. The existing seeded-defect checks (`pdd.sh
capture --job controlled`) still patch the same text: the `late-scratch` anchor
`Protocol.Admit(b0, b1, ext, (uint)count, state, total, ...` and the `control-total`
anchor `total = size;` / `obs.OnNext(frame);` each still occur exactly once.

## 5. `boundaries.json` after the adapter

Not edited. Proposed wording only.

| Entry | Now enforced by the types | Unchanged | Proposed wording |
| --- | --- | --- | --- |
| CLOSE-LOCK | "initial state is zero and events are 0..2": `_closeState` is a `CloseState` whose default is 0 and whose other values come only from `CloseStep`; an event is a `CloseEvent`, which has three values. `AdvanceClose(3)` no longer compiles. | All `CloseStep` calls serialize under `_stateLock`. | "All CloseStep calls serialize access to the same sender state under _stateLock. The state and event ranges are carried by CloseState and CloseEvent (Helper/CoreBoundaryTypes.cs); they rely on the C# type system, not on callers." |
| HEADER-ADAPTER | "the parser's message state": a `ReceiveState`, so in {0, 1, 2, 8}. | The retained total and `total <= limit`; "exactly the extended-length bytes read after them with their count"; the configured limit; read order on status 0 and 2. | "ReadDataframeAsync passes Admit the two prefix bytes, exactly the extended-length bytes read after them with their count, the configured limit, the parser's message state as a ReceiveState and its retained total, which does not exceed the limit; it reads more header bytes only on status 0 and reads or allocates the payload only on status 2." |
| REASSEMBLY-TOTALS | Nothing. `Step`, which computes the total, now takes a typed state. | The total equals the sum of the segment lengths: a relation. | None |
| MASK-ADAPTER | Nothing (byte loops, out of scope). | All | None |
| PAYLOAD-OWNERSHIP, NATIVE-COPY | Nothing | All (aliasing, ownership, `Buffer.BlockCopy`) | None |
| CLOSE-WRITE, CLOSE-FAILURE, CLOSE-TIMER, CLOSE-OBSERVER | Nothing: their assumptions have no value part (section 2). | All | None |
| WRITE-PROGRESS, EPOCH-PROGRESS | Untouched: they cover no core call. | All | None |

Section 2 found that no entry states D5's `TxState` precondition. It is now a property
of `TransmitState`, so it no longer needs an entry. D7, D8, D10 and D11 still have no
owner. D8, D10 and D11 have no precondition, and the adapter does not change D7's owner.

## 6. After numbers

Local measurements on the same machine as section 3, `DOTNET_TieredCompilation=0`,
Release. Every timed run used `flock ~/worktrees/.timing.lock`. Each `.meta` holds the
process list (`ps -eo pid,etime,args | grep -E 'claude -p|dotnet' | grep -v grep`,
reduced on 2026-11-11 to pid, etime and process name; see the evidence
[README](../docs/evidence/core-boundary-types/README.md)) and the load taken inside
the lock just before the run. Other sessions (S6 and the #140 fix
session) were running; the one-minute load average was 0.65 to 1.60, against 0.58 to
0.73 for the baseline. Every run is reported. The first after runs measured
`6646466`, an earlier version of the production commit. They are kept in
`after/superseded-6646466/` with a note (section 8, decision 8).

### The README benchmark, three runs (`after/benchmark-run-N.*`)

The benchmark compiles `VerifiedNativeCopy.cs`, `Masking.g.cs` and
`WebsocketMasking.cs`. None of them differs between `d5c648d` and `13ed44f`
(`git diff --stat` is empty), so the change cannot affect these numbers. They were
rerun as the brief asks. Verified medians in ns, runs 1 / 2 / 3; "previous" is the
benchmark's unchanged scalar reference in the same run.

| Kernel | Bytes | Verified before (S7) | Verified after | Previous, after runs | Allocated after |
| --- | ---: | --- | --- | --- | --- |
| `Masking.Apply` | 0 | 2.61 / 2.58 / 2.58 | 2.75 / 2.75 / 2.58 | 1.90 / 1.90 / 1.78 | 0 |
| `Masking.Apply` | 4 | 3.29 / 3.21 / 3.25 | 3.34 / 3.59 / 3.33 | 2.54 / 2.53 / 2.37 | 0 |
| `Masking.Apply` | 125 | 35.43 / 35.86 / 35.01 | 37.25 / 37.42 / 34.94 | 36.28 / 36.24 / 33.94 | 0 |
| `Masking.Apply` | 1024 | 284.32 / 282.48 / 281.66 | 299.63 / 301.65 / 281.45 | 297.64 / 297.60 / 278.87 | 0 |
| `Masking.Apply` | 65536 | 17402.09 / 17385.86 / 17345.90 | 18469.14 / 18463.30 / 17305.01 | 18468.92 / 18461.46 / 17289.59 | 0 |
| `Masking.Apply` | 1048576 | 277896.28 / 277893.31 / 277662.95 | 295140.52 / 295523.65 / 276429.48 | 295111.55 / 295484.35 / 276528.48 | 0 |
| `CopyMasked` | 125 | 44.16 / 44.15 / 44.24 | 46.97 / 46.97 / 44.18 | 54.48 / 54.49 / 51.34 | 0 |
| `CopyMasked` | 1024 | 369.07 / 372.59 / 366.08 | 388.92 / 391.65 / 367.49 | 441.48 / 441.72 / 416.96 | 0 |
| `CopyMasked` | 65536 | 22994.08 / 22973.08 / 22956.65 | 24446.05 / 24466.11 / 22964.74 | 27671.12 / 27714.82 / 25960.54 | 0 |
| `MaskSlice` | 125 | 51.62 / 51.70 / 51.60 | 54.86 / 54.96 / 51.56 | 58.16 / 58.21 / 59.76 | 0 |
| `MaskSlice` | 1024 | 427.83 / 425.46 / 428.88 | 455.38 / 452.53 / 426.44 | 484.19 / 487.08 / 454.96 | 0 |
| `MaskSlice` | 65536 | 26533.27 / 26551.39 / 26532.98 | 28202.36 / 26574.97 / 26547.70 | 30112.44 / 28239.99 / 28304.65 | 0 |
| `Merge` (bulk) | 1024 | 40.17 / 40.21 / 39.93 | 43.34 / 41.76 / 41.23 | `BlockCopy` 41.06 / 39.59 / 38.92 | not reported |
| `Merge` (bulk) | 65536 | 1071.55 / 1077.77 / 1068.76 | 1136.04 / 1096.60 / 1081.88 | `BlockCopy` 1153.51 / 1087.61 / 1065.37 | not reported |
| `MergeScalar` | 1024 | 306.16 / 303.46 / 299.09 | 312.59 / 306.43 / 304.49 | | not reported |
| `MergeScalar` | 65536 | 14836.42 / 14892.28 / 14835.32 | 15790.08 / 14939.75 / 14894.43 | | not reported |

After runs 1 and 2 are 5 to 7% slower than the baseline in almost every row, and the
unchanged previous-scalar reference is slower by the same amount in the same runs,
so the verified-to-previous ratios match the baseline (for example 1.000 at
1 MiB, 0.883 for `CopyMasked` at 65536). Run 3 is within 0.5% of the baseline in
every masking row. The shift is the machine under the parallel sessions' load, not the
code, whose inputs are identical. Every masking row allocated 0 bytes. S8 compares
medians run by run, as section 3 asks.

### The decision path (`decision-path/`)

The README benchmark runs only the byte loops (section 8, decision 1). To measure the
per-frame paths that the typed calls sit on, `decision-path/Program.cs` compiles the
library source into a probe. It reads 20,000 frames from a `MemoryStream` through
`WebsocketParserHandler.DataframeObservable` (D1-D3, H3), and sends 20,000 frames
through `WebsocketSenderHandler` with a write delegate that does nothing (D5, D9, E1,
H1, H2). Each scenario has one warm-up and seven trials; the median is reported.
Allocation is `GC.GetTotalAllocatedBytes(true)` over a trial divided by the frames,
setup included. `run.sh` built the probe against a `git archive` of `d5c648d`
(before, recorded before any code change) and of `13ed44f` (after), and ran each
three times.

| Scenario | ns/frame before, runs 1 / 2 / 3 | ns/frame after | Bytes/frame before | Bytes/frame after |
| --- | --- | --- | ---: | ---: |
| receive binary 16 B | 136.2 / 135.4 / 115.6 | 111.0 / 110.7 / 119.2 | 208.26 | 208.26 |
| receive text 125 B | 151.4 / 122.8 / 127.3 | 122.2 / 123.5 / 125.6 | 320.28 | 320.28 |
| receive binary 200 B (16-bit length) | 163.7 / 160.8 / 168.7 | 158.3 / 159.9 / 162.5 | 392.28 | 392.28 |
| receive fragmented 2 × 8 B | 125.1 / 126.3 / 130.8 | 126.6 / 123.9 / 123.9 | 216.28 | 216.28 |
| receive Ping + text 16 B | 106.2 / 108.1 / 112.2 | 107.6 / 105.6 / 107.6 | 180.28 | 180.28 |
| send binary 16 B | 688.5 / 689.9 / 734.0 | 683.4 / 706.2 / 692.6 | 104.12 | 104.12 |
| send text 125 B | 783.7 / 787.0 / 831.7 | 777.4 / 780.7 / 778.4 | 104.14 | 104.14 |

Bytes per frame are identical before and after in every scenario and in every trial
(minimum equals maximum in each `.out`). So the change adds no allocation on these
per-frame paths. The times overlap: the run-to-run spread, up to 19% before, is larger
than any difference between before and after. The probe does not isolate the
decision calls; it bounds their cost inside the real path.

### Lines

| Scope | Total lines | Code lines |
| --- | ---: | ---: |
| `Helper/CoreBoundaryTypes.cs` (new) | 111 | 56 |
| `Helper/DataframeParsing.cs` | 90 → 102 | 75 → 82 |
| `Service/WebsocketParserHandler.cs` | 73 → 73 | 65 → 65 |
| `Service/WebsocketSenderHandler.cs` | 492 → 492 | 417 → 417 |
| Production, net | +124 | +63 |
| `CoreBoundaryTypesTests.cs` (new) | 190 | 177 |

"Code" counts lines that are neither blank nor only a `//` or `///` comment, as in
section 3. Calls from production straight into a typed generated method
(`Step`, `Admit`, `SendStep`, `SendFailed`, `CloseStep`) went from 7 call expressions
(D1-D7) in 3 files to the 2 `Admit` lines, which take a `ReceiveState`, plus the 4
forwarding calls inside the wrappers.

### Redundant tests

None. No existing test passed an out-of-range state or event to production code;
the preconditions made that undefined. `ProtocolCoreTests` and `ProtocolStateTests`
call the generated core with raw codes on purpose and still test the core itself.
`ParserRetentionTests` and `PayloadParsingTests` use the raw entry point, which still
exists. No test was changed or deleted.

### Checks in S8

Run on `76564f1`, a clean tree (`git status --porcelain` empty before and after), in
this order, as `artifacts/s8/checks.txt` records (copied to `after/checks/`). These
were not timed measurements and did not take the timing lock; no duration from them
is reported as a measurement.

| Check | Command | Result |
| --- | --- | --- |
| Build | `dotnet build src/main/WebsocketClientLite.sln -c Release` | run and passed, 0 warnings, 0 errors |
| Pdd tests | `dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release` | run and passed |
| Requirement map | `./scripts/pdd.sh validate` | run and passed, 16 requirements |
| Verifier and generated-code check | `capture --job protocol` (`./scripts/dafny-masking.sh --check`) | run and passed. Last line: `Dafny program verifier finished with 41 verified, 0 errors` |
| Dafny mutations | `capture --job protocol` (`./scripts/pdd.sh mutations`) | run and passed: baseline passed, 17 of 17 detected. No mutant took more than a third of its budget (lesson 3). |
| Five-target matrix | `capture --job tests` (`./test-all-tfms.sh Release`) | run and passed, 280 of 280 on each of the five library builds (host .NET 10.0.14) |
| Coverage gate | `capture --job tests` (`./scripts/check-coverage.sh ... 70`) | run and passed, line coverage 83.5% |
| Controlled and seeded-defect checks | `capture --job controlled` | run and passed: controlled defect, 13 boundary defects, queued defect, 3 lease defects, 2 retention defects (`control-total`, `late-scratch`), reconnect defect |
| Stryker | `capture --job stryker` | run and passed against the recorded dispositions. 42 killed, 27 survived, 7 no coverage, 10 compile errors, 163 ignored; score 55.26%. Stryker mutates only `WebsocketConnectionHandler.cs`, which S8 does not change. No Stryker run was made on the base of this branch, so S8 cannot show whether any outcome changed. The line 95 `throw handshakeException;` mutant was killed in this run; lesson 10 says that kill can be a race, so this one run does not show it is reliably killed. |
| Aggregate | `./scripts/pdd.sh aggregate` | run and passed: "Machine evidence gate: passed" |
| Plain tests of the adapter | inside the matrix; also alone in each seeded copy | run and passed (11 of 11) |
| Benchmark | README command, three runs under the lock | run and passed (completed; numbers above) |
| CI | `gh workflow run ci.yml` | not run by S8 (decision 11) |

## 7. Recommendation

**Adopt.** The criterion is additional assurance per unit of maintained machinery.

Assurance gained:

- Five value preconditions of the generated core no longer rest on a data-flow
  argument nobody checks: the receive state of `Step` and both `Admit` calls, the
  transmit state of `SendStep` and `SendFailed`, and the close state and event of
  `CloseStep`. They now hold for any code that compiles in safe C#. Before, they held
  too, by the argument in section 1, so no defect was found. The gain is that a later
  edit cannot break them silently: `AdvanceClose(3)`, `_closeState = 9`,
  `_sendState = 4` or `ReadDataframeAsync` with state 5 from production no longer
  compile. (In S8 the last one compiled, through the raw entry point that was then in
  the production assembly, and failed at run time. Since M4 that entry point is in the
  test project, so the claim holds; section 9, finding 1.)
- The value part of CLOSE-LOCK and the message-state part of HEADER-ADAPTER can leave
  the assumption register (section 5), and the `TxState` gap S7 found is closed
  without a new entry.
- It is not only a renaming: the compiler rejects construction outside the types, the
  closure tests show that every wrapper returns what the core returns, and since M4 a
  plain test pins the members inside each type that construct a value (section 9).

Machinery: one 111-line file of 56 code lines, 7 code lines for the raw entry point, 177
code lines of plain tests that run in about 30 ms. No allocation, no measurable time
change, no new dependency, no change to the Dafny source, the generated file, any
existing test, `tools/Pdd` or the seeded-defect anchors. The wrappers mirror five
generated signatures; a change to one of those signatures fails the build at the
wrapper, which is where it would need attention anyway.

Weaker than the brief hoped:

- Only ranges are enforced. REASSEMBLY-TOTALS and MASK-ADAPTER, two of the brief's
  three examples, are relations and are untouched, as S7 predicted. So is
  `total <= limit` in HEADER-ADAPTER.
- The raw entry point stays for the tests, with a run-time check. (Since M4 it is in
  the test project.)
- `ReceiveState` converts implicitly to `byte`, so that the `Admit` lines keep the text
  a seeded defect patches. (Removed at M4; section 9.)
- `default(CloseEvent)` is the legal event `Send`. (Fixed at M4; section 9.)

## 8. Decisions for owner review (S8)

1. **The benchmark covers only the byte loops (S7 decision 3).** Decided, not asked.
   The README benchmark ran three times unchanged, matching S7. In addition,
   `docs/evidence/core-boundary-types/decision-path/` is an evidence-only probe of the
   per-frame receive and send paths, before and after, under the timing lock. It is
   not repository tooling: no script, CI job, `tools/Pdd` code or check uses it, and
   it builds in a temporary directory. Deleting the directory removes it. The probe
   measures the paths, not the decision calls in isolation.
2. **Which calls are typed.** `Step`, `Admit`, `SendStep`, `SendFailed` and `CloseStep`,
   the five with a value range in `requires`. `Outgoing`, `CloseValid` and
   `HandshakeValid` have no `requires`; typing them would only rename.
3. **Results are typed only where they are fed back as inputs** (the next states).
   `Admit`'s status stays a byte compared with 0 and 2; `status <= 2` is ensured, so a
   type would only rename it.
4. **`total <= limit` is not typed.** A `ReceiveCursor` holding state, total and limit,
   made only by a start value and by `Step`, would enforce it by construction. It
   would replace the `total = size;` line that the `control-total` seeded defect in
   `tools/Pdd/RetentionDefects.cs` patches, and it would make that defect
   unrepresentable. Changing a seeded-defect anchor is a tooling change the plan does
   not authorize, so it is proposed here, not done.
5. **The implicit conversion `ReceiveState` → `byte`** exists so that the two
   `Protocol.Admit` lines keep their exact text, the `late-scratch` anchor. A
   conversion out of the type is always legal; there is none into it. (Superseded at
   M4: removed, and the anchor text kept another way; section 9, finding 2.)
6. **The raw-code entry point.** `ParserRetentionTests` passes `(ct, 2, retained)` and
   `PayloadParsingTests` passes `(ct)`. Tests stay unchanged, so
   `DataframeParsingRawState.ReadDataframeAsync` keeps that signature and checks the
   code at run time. It is a separate class because an overload in `DataframeParsing`
   made `pdd.sh validate` fail ("2 declarations") and 79 Pdd tests with it. No
   requirement entry was edited. (Superseded at M4: the class is in the test project;
   section 9, finding 1.)
7. **The plain tests are in the production commit**, so that the revert at M4 (owner
   decision 2026-11-11, item 3), if any, leaves a tree that builds.
8. **The production commit was rewritten once before the first push.** `6646466` had
   the overload of decision 6 inside `DataframeParsing`. The fix was folded in, giving
   `13ed44f`, and every after run was repeated. The runs on `6646466` are kept in
   `after/superseded-6646466/`. Their three benchmark `.meta` files were overwritten
   by mistake; the note there records what the console showed and that the `ps`
   output is lost.
9. **Named members replace raw reads**: `IsIdle`, `IsClosed`, `ReceivedOrTerminal`,
   `Started`. Each has the same expression as before. They are hand-written, not
   verified.
10. **Not proposed in `requirements.json`:** the new tests are not obligations of any
    entry. If the owner adopts the boundary wording in section 5, CLOSE-001 and
    HEADER-002 could name `CoreBoundaryTypesTests`. That is the owner's change.
11. **CI was not dispatched by S8.** The controller dispatches it on the final commit at
    M4 (owner decision 2026-11-11, item 2).

## 9. M4 review fixes (2026-11-11)

Written 2026-11-11 for merge point M4, after the `/code-review high` of PR #143 (inline
comments r4544078277 to r4544079322), under the owner decision and the controller
decision of 2026-11-11 posted on #143. Production commit: `3519275`, on top of the S8
commits, holding the production change and its plain tests only. Evidence:
[`docs/evidence/core-boundary-types/after-review/`](../docs/evidence/core-boundary-types/after-review/).
No change to the Dafny sources, the generated file, any test that existed before S8,
`tools/Pdd`, the `tools/Pdd` seeded-defect anchors, `boundaries.json`,
`requirements.json` or the requirements schema.

### What each finding changed

| # | Finding | What was done | Commit |
| --- | --- | --- | --- |
| 1 | The raw `ReadDataframeAsync(ct, byte state, uint total)` was in the production assembly, so section 7's "state 5 from production no longer compiles" was false | `DataframeParsingRawState` moved to the test project (`TestConnections.cs`). Production has no entry point that takes a raw state, so the claim is now true. `DataframeParsing.ReadDataframeAsync` stays one declaration; `pdd.sh validate` passes | `3519275` |
| 2 | The implicit `ReceiveState` → `byte` conversion let a state flow into any byte parameter, such as `CloseStep`'s | Removed. `ReadDataframeAsync` takes `messageState.Code` once into a local `byte state`, so the two `Admit` lines keep their exact text, the `late-scratch` anchor, with no tooling change. Comparisons such as `state == 8` no longer compile | `3519275` |
| 3 | The `.meta` files held full `ps -eo args` lines with other sessions' prompts | Every committed `.meta` reduced to pid, etime and process name; the scripts record `ps -eo pid,etime,comm`; note in the evidence README. Timings and other fields unchanged. History not rewritten | `d13be1c` |
| 4 | The raw entry point passed `total` unchecked to `Admit`, which requires `total <= limit` | It rejects a total above `MaxFrameSize` with `ArgumentOutOfRangeException` before reading. New test `RawReadRejectsATotalAboveTheLimitBeforeReadingAnyByte` (total = limit still reads) | `3519275` |
| 5 | The raw entry point threw synchronously, where the old `async` method faulted its task | It is `async` again. Both raw-read tests assert that the task is faulted, not that the call throws | `3519275` |
| 6 | `bench.sh` ran `cd /home/vscode/worktrees/s7` and no `.meta` named the measured commit | It benchmarks the caller's git tree and records `git rev-parse HEAD` and the changed-file count inside the lock; `decision-path/run.sh` records the source commit and its real exit code. S7 and S8 both worked in `~/worktrees/s7` (the paths in `after/checks/stryker.log` and `solution-build.log`), so S8's after runs measured S8's tree, but which commit is not recorded inside those runs | `65666dc` |
| 7 | `default(CloseEvent)` was `Send`, so an uninitialized event requested a Close | `CloseEvent` stores its code plus one. `default(CloseEvent).Code` throws `InvalidOperationException`, and with it `CloseState.Step`. New test `DefaultCloseEventIsNoEvent` | `3519275` |
| 8 | `OnlyPrivateConstructorsAndOneByteField` does not show that no member inside a type constructs an illegal value | New test `OnlyTheListedMembersCallThePrivateConstructor`: it scans the IL of every method of each type and its nested types for calls to the private constructor, and requires exactly an allow-list (`FromCode`, `Step`; `SendStep`, `SendFailed`; `Step`; the three named events). Section 4's claim is reworded to what is enforced (below) | `3519275` |
| 9 | The new test copied the `_stream` reflection that about 10 test files hold | A shared `TestConnections.Over(stream, limit)` builds the connection for the new tests. The copies in tests that existed before S8 are not consolidated (decision 3 below) | `3519275` |
| 10 | The four types used three shapes | One shape: `private readonly byte _code;` and `internal byte Code`. The reflection test checks that `Code` exists and that no type has a conversion operator | `3519275` |

### What the types enforce now, stated exactly (finding 8)

- **Enforced by the C# compiler:** outside the four types, safe C# can make a value only
  through their members or `default`. The constructor is private and there is no
  conversion into or out of any type, so leaving a type is always a visible `.Code`.
  `default` is code 0 for the three state types, which every state range contains, and
  is no event for `CloseEvent`: using it throws.
- **Enforced by a plain test, not by the compiler:** which members inside each type call
  its private constructor. A new member that does fails
  `OnlyTheListedMembersCallThePrivateConstructor` until it is added to the allow-list.
  The test shows who constructs, not that a listed member constructs only legal codes.
  That is shown member by member: `FromCode` over all 256 bytes, each wrapper against the
  generated method it forwards to over every reachable state, and the named events by
  value. A wrapper's result is legal because the generated method's `ensures` says so.
- **Not enforced:** reflection, `Unsafe.As` and unsafe code; the relations
  (`total <= limit`, REASSEMBLY-TOTALS, MASK-ADAPTER); and, inside
  `DataframeParsing.ReadDataframeAsync`, the local `byte state`, which is the one place
  the receive state leaves its type, for the two `Admit` calls.

### After-review numbers (local measurements)

Same machine and command as sections 3 and 6, under `flock ~/worktrees/.timing.lock`,
on `3519275` as recorded inside each run. Other sessions were running untimed work:
the one-minute load average was 15.16, 14.32 and 2.66 for the three benchmark runs and
38.93, 39.19 and 9.16 for the probe runs, against 0.58 to 0.73 for the baseline. Every run is
reported.

- **README benchmark** (`after-review/benchmark-run-N.*`). Its three source files are
  unchanged since `d5c648d`. Every verified median is inside S7's run-to-run range or
  within 1.7% of it; the largest difference is `Merge` at 65536 bytes in run 2,
  1095.28 ns against S7's 1068.76 to 1077.77. Every masking row allocated 0 bytes.
  Verified medians in ns, runs 1 / 2 / 3: `Masking.Apply` 1024 B 281.68 / 281.35 /
  282.41 (S7 281.66 to 284.32); `Masking.Apply` 1 MiB 281226.80 / 276600.51 /
  278063.34 (S7 277662.95 to 277896.28); `CopyMasked` 65536 B 22974.29 / 22991.90 /
  23039.67 (S7 22956.65 to 22994.08); `MaskSlice` 65536 B 26498.28 / 26400.69 /
  26506.88 (S7 26532.98 to 26551.39). The raw output has every row.
- **Decision-path probe** (`after-review/decision-path/`), built from `git archive
  3519275`, three runs. The median bytes per frame equal S8's before and after values in
  all seven scenarios (receive 208.26 / 320.28 / 392.28 / 216.28 / 180.28, send
  104.12 / 104.14). In run 1, one trial of "receive text 125 B" measured 320.26 bytes
  per frame, below the median; every other trial of every run equals its median. So
  section 6's "minimum equals maximum in each `.out`" does not hold for that run; the
  lower value is not an added allocation. Times overlap S8's before and after runs
  (for example receive binary 16 B 111.1 / 109.8 / 120.0 ns, send binary 16 B 678.5 /
  692.3 / 736.0 ns).
- **Lines.** `CoreBoundaryTypes.cs` 111 → 118 lines, 56 → 59 code lines.
  `DataframeParsing.cs` 102 → 92 lines, 82 → 76 code lines (the raw entry point left).
  Production net against `d5c648d`: +120 lines, +60 code lines. Tests:
  `CoreBoundaryTypesTests.cs` 190 → 263 lines, 177 → 234 code lines, and
  `TestConnections.cs` (new, with the raw entry point) 48 lines, 34 code lines.

### Seeded adapter defects (`after-review/seeded/`)

`seed.sh` is S8's script with three more defects, run on `3519275` under the lock. Each
copy runs only `CoreBoundaryTypesTests` (17 tests).

| Defect | Edit | Result |
| --- | --- | --- |
| none (control) | no change | 17 of 17 passed |
| `fromcode-accepts-3` | `FromCode` also accepts 3 | 2 failed: `FromCodeAccepts...`, `RawReadRejectsAnIllegalState...` |
| `received-is-event-2` | `CloseEvent.Received` is code 2 | 2 failed: `DefaultIsCodeZero...`, `WrappersDoNotAllocate` |
| `sendstep-keeps-state` | `SendStep` returns the old state | 2 failed: `TransmitStepMatches...`, `OnlyTheListedMembers...` (`TransmitState`) |
| `step-drops-state` | `Step` passes state 0 to the core | 2 failed: `ReceiveStepMatches...`, `WrappersDoNotAllocate` |
| `extra-factory` (finding 8) | adds `CloseState.From(byte code) => new(code)` | 1 failed: `OnlyTheListedMembers...` (`CloseState`): expected `["Step"]`, actual `["From", "Step"]` |
| `implicit-conversion` (finding 2) | restores `implicit operator byte(ReceiveState)` | 1 failed: `OnlyPrivateConstructorsAndOneByteField` (`ReceiveState`) |
| `default-event-is-send` (finding 7) | `default(CloseEvent).Code` returns 0 | 1 failed: `DefaultCloseEventIsNoEvent` |

The plain tests alone catch each one. S8's four anchors still occur exactly once, and
so do the `tools/Pdd` anchors (`late-scratch`, `control-total`), which the controlled
capture job below patched.

### Checks at M4

Run on `7415369`, a clean tree (`git status --porcelain` empty before and after), each
command under `flock ~/worktrees/.timing.lock`, as `after-review/checks/checks.txt`
records. These were not timed measurements; no duration from them is reported as one.
A first attempt was stopped by this session 16 seconds in, during the build, because
its background time limit was too short; no check had completed (kept locally as
`artifacts/m4-stopped-1`).

| Check | Result |
| --- | --- |
| Build, Release | run and passed, 0 warnings, 0 errors |
| Pdd tests | run and passed, 262 of 262 |
| `./scripts/pdd.sh validate` | run and passed, 16 requirements |
| Verifier and generated-code check (`capture --job protocol`, `dafny-masking.sh --check`) | run and passed: `Dafny program verifier finished with 41 verified, 0 errors` |
| Dafny mutations (`capture --job protocol`) | run and passed: baseline passed, 17 of 17 detected; the slowest took 9.8 s, below a third of its budget (lesson 3) |
| Five-target matrix (`capture --job tests`) | run and passed, 286 of 286 on each of the five library builds |
| Coverage gate (`capture --job tests`) | run and passed, line coverage 83.5% |
| Controlled and seeded-defect checks (`capture --job controlled`) | run and passed: controlled defect, boundary, queued, lease, retention (`control-total`, `late-scratch`) and reconnect defects |
| Seeded adapter defects (`after-review/seeded/`) | run and passed: 7 of 7 caught by the plain tests alone, control 17 of 17 |
| Stryker (`capture --job stryker`) | run and passed against the recorded dispositions: 41 killed, 28 survived, 7 no coverage, 10 compile errors, 163 ignored; score 53.95%. S8's run had 42 killed. The difference is the line 95 `throw handshakeException;` mutant in `WebsocketConnectionHandler.cs`: killed in S8's run, survived here. That file and `ReconnectTests.cs` are identical to `c9701aa`, where lesson 10 found the kill to be a race. As lesson 10 asks, the covering test `ReconnectTests.FailedConnectTerminatesAndRemovesRegistration` ran 10 times, plain and under the lock, against a `git archive` copy of `7415369` with the statement removed (`after-review/checks/line95-mutant/`): 1 of 10 failed (`Sender` was set), 9 passed. The kill depends on timing, as lesson 10 found (0 of 15 plain there). This branch does not touch the file; S11 owns the fix. The loaded runs of lesson 10 were not repeated |
| Aggregate | first run interrupted: it waited for the lock from 00:47:52Z and the session's 2-hour background limit stopped the script at about 01:19:47Z, before it ran (log kept). Second run, without the lock because it is not timed: run and passed, "Machine evidence gate: passed" |
| CI | dispatched with `gh workflow run ci.yml --ref chore/core-boundary-types` on the branch head that adds this table; its run ID and conclusion are in the M4 receipt on #143, not here, so that the measured commit stays the head |

### Recommendation after the review

**Unchanged: adopt.** No finding weakens the result. Findings 1, 2, 7 and 8 remove three
of the four limits section 7 listed as "weaker than the brief hoped" (the raw entry point
in production, the implicit conversion, `default(CloseEvent)`) and make the claim about
construction exact. Production went from +63 to +60 code lines against `d5c648d`, because
the raw entry point left it, and the plain tests grew by 91 code lines
(`CoreBoundaryTypesTests.cs` and `TestConnections.cs`), with no allocation and no
measurable time on the measured paths.

### Decisions for owner review (M4)

1. **Finding 2 was fixed without a tooling change.** The `late-scratch` anchor names a
   variable `state`, so `ReadDataframeAsync`'s parameter is now `messageState` and a
   local `byte state = messageState.Code;` feeds the two `Admit` lines. Inside that one
   method the code is a byte again; this is stated above as not enforced.
2. **Finding 8 was fixed in a test, not by the compiler.** The allow-list makes adding a
   constructing member a visible edit; it does not prove the new member correct.
3. **Finding 9 is fixed for the new tests only.** About 10 test files that existed
   before S8 keep their own `_stream` reflection. Moving them onto
   `TestConnections.Over` would change existing tests, which the brief keeps unchanged.
   It is a follow-up for the owner to allow.
4. **Finding 3: no history was rewritten**, as the controller decided. The full
   process lines stay in this branch's history (commits up to `f5d49f2`) until the
   branch is deleted, which is the owner's call. After a squash merge the release
   branch has only the reduced files. Section 3's remark that the only other `claude -p`
   process was a code review session can no longer be checked from the files.
5. **Finding 6: S8's after runs are not rerun as a correction.** They measured S8's
   tree (see the table), and their commit is recorded only outside the runs
   (`after/source-commit.txt`). The M4 runs record it inside.
6. **`default(CloseEvent)` now throws** inside `AdvanceClose`, under `_stateLock`. Only
   a programming error reaches it; every legal event behaves as before.
7. **Measured under load.** The after-review runs ran while other sessions did untimed
   work (one-minute load up to 39.19). The lock kept other timed runs out, not other work.
8. **Stryker line 95 survived in this run**, where S8's run killed it. It is lesson 10's
   race in a file this branch does not change; 1 of 10 plain runs of the covering test caught the mutant. It is
   not counted as a regression of this branch, and S11 replaces the racing test.
