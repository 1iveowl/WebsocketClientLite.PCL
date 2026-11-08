# Parser retention and scratch ownership (P3-C)

This is tested C# adapter evidence for REASSEMBLY-TOTALS and PAYLOAD-OWNERSHIP,
not an extension of the Merge proof. The production parser, header reader and
handshake reader are unchanged. The specification is P3-C in planning commit
78966de (`docs/pdd-phase-3-plan.md`), read from the planning branch; the release
baseline is 77af6c5, containing G and merged A/B.

`ParserRetentionTests` uses the existing private stream injection convention and
one-byte stream reads through both target-dependent ReadAsync overloads. There is
one subscription, after handshake completion. No concurrent reader is supported:
`ReadHeaderBytesAsync` returns an eight-byte shared scratch array, valid only until
the next header read. Handshake consumes each byte synchronously; header parsing
captures b0/b1 before the extended-length read overwrites the array. Payload reads
allocate distinct storage. Retained payloads stay stable until emission; the
one-segment fast path reuses its payload, so fresh output is not promised for
all parser messages. No new pool/copying policy is introduced.

The expected bytes come from literal input arrays and LINQ concatenation, never
from Protocol.Step, Admit, Merge or a production encoder. The wire builder handles
only the small literal frames used here; extended headers are literal bytes.

- 54 combinations: Text/Binary, three fragment lengths each in 0..2, interleaved
  Ping/Pong, empty continuations, then consecutive Binary and empty Text messages.
  Checks bytes, Length, FIN, Fragment and original opcode, covering zero/one/many
  nonempty retained arrays and reset without stale data.
- Limits 1 and 4: each retained prefix 0..N, complete totals N−1/N/N+1 where
  possible, empty continuation and both control payloads. Successful output and
  the next admission's remaining budget observe retained totals. Rejected payloads
  are present in the input; exact read-attempt and byte counts prove rejection
  before their consumption, not an accidental EOF failure.
- Header-only rejection at int.MaxValue plus a retained byte, int.MaxValue+1,
  and the forbidden 64-bit high bit; no enormous allocation is attempted.
- Close abort, orphan continuation before/after a completed message, EOF and
  cancellation at next header, partial extended header and partial payload.
  Cancellation here is the transport's OperationCanceledException outcome; these
  cases do not claim exhaustive subscription-disposal scheduling.
- Split UTF-8 succeeds only after complete assembly; an invalid final byte fails
  without emitting partial data. The control can still be observed first.
- A real production handshake, with its random request key answered by the test
  writer, stops exactly at the HTTP terminator before parsing the adjacent frame.
  A canonical 126-byte frame checks base-header capture across scratch reuse.

`retention-defects` uses isolated source copies and the existing PDD process/TRX
validators. It first passes all 13 cases plus the runtime probe, then requires:

| Edit | Test | Required assertion |
| --- | --- | --- |
| Add control payload length to retained total | ControlPayloadDoesNotChangeRetainedTotal | Control payload changed retained budget |
| Use header[0]/header[1] after extended read instead of captured b0/b1 | ExtendedHeaderCapturesScratchBeforeReuse | Header scratch capture corrupted admission |

These faults are non-equivalent: one corrupts inter-frame size accounting; the
other consumes overwritten header storage within a frame. Compile failures,
timeouts, interruption, missing TRX/cases, survival, wrong edits/source/runtime,
and unrelated assertions do not count. Release/net10.0 is checked explicitly.
Controlled capture and aggregation require both detections; all five library
matrix cells require the named adapter tests under REASSEMBLY-001-TEST. Existing
G/A/B experiments and F Merge/native/masking tests remain required unchanged.

No genuine production defect has been reproduced in this increment. Artificial
negative edits are evidence checks, not reports of shipping defects.

Limits: finite small sequences and selected admission boundaries; no arbitrary
schedule, full RFC, memory-model, native-copy, Rx/runtime or allocation proof.
Single-reader discipline and external stream behavior remain trusted. Byte caps
do not bound fragment/object count or total process memory; no fragment-count
policy is added. Internal locals are not introspected: total/list agreement is
observed via emitted bytes/metadata and subsequent admission/read budgets.
Human acceptance of these assumptions remains revision-bound PR review.
