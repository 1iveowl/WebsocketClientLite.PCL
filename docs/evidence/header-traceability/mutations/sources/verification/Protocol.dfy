module {:extern "WebsocketClientLite.VerifiedProtocol"} ProtocolCore {
  newtype {:nativeType "uint"} Size = n: int | 0 <= n <= 0x7fffffff
  class Protocol {
    // RFC 6455 section 5.2. Multibyte length quantities are in network byte order:
    // Word reads the bytes most significant first.
    static ghost function Word(s: seq<bv8>): bv64 {
      if |s| == 0 then 0 else (Word(s[..|s|-1]) << 8) | (s[|s|-1] as bv64)
    }

    // RFC 6455 section 5.2. Reads the 16-bit or 64-bit extended payload length in
    // network byte order.
    static method ReadLength(bytes: array<bv8>, count: Size) returns (value: bv64)
      requires count <= 8 && count as int <= bytes.Length
      ensures value == Word(bytes[..count as int])
    {
      if count == 0 { value := 0; }
      else {
        var prefix := ReadLength(bytes, count - 1);
        assert bytes[..count as int][..count as int - 1] == bytes[..count as int - 1];
        value := (prefix << 8) | (bytes[count - 1] as bv64);
      }
    }

    // RFC 6455 section 5.2. The defined opcodes: 0x0 continuation, 0x1 text, 0x2 binary,
    // 0x8 Close, 0x9 Ping, 0xA Pong. 0x3-0x7 and 0xB-0xF are reserved, and an unknown
    // opcode MUST fail the connection.
    static function Known(op: bv8): bool { op == 0 || op == 1 || op == 2 || op == 8 || op == 9 || op == 10 }
    // RFC 6455 sections 5.1, 5.2 and 5.5, mixed with library policy. Section 5.2: RSV1-3
    // MUST be 0 unless a negotiated extension defines them, and the opcode must be known.
    // Library policy: the client negotiates no extensions, so every RSV bit must be 0.
    // Section 5.1: a client MUST close the connection if it detects a masked frame.
    // Section 5.5: a control frame (opcode 0x8 or above) MUST NOT be fragmented, so FIN is
    // set, and its payload length is 125 bytes or less.
    static function PrefixValid(first: bv8, second: bv8): bool {
      var op := first & 15;
      (first & 112) == 0 && (second & 128) == 0 && Known(op) &&
      (op < 8 || ((first & 128) != 0 && (second & 127) <= 125))
    }
    // RFC 6455 sections 5.2 and 5.5, mixed with library policy. Section 5.2: the minimal
    // number of bytes MUST be used to encode the length (marker 0-125 is the length, 126 is
    // followed by 126-65535 in 16 bits, 127 by 65536 or more in 64 bits), and the most
    // significant bit of the 64-bit length MUST be 0. Section 5.5: a control frame has FIN
    // set and a length of 125 bytes or less. Library policy: the length is at most the
    // configured limit (section 10.4 asks for a frame size limit but sets no value) and at
    // most 0x7fffffff, the largest .NET array. That bound implies the bit-63 clause, which
    // stays as written.
    static function HeaderValid(first: bv8, second: bv8, length: bv64, limit: bv64): bool {
      var op := first & 15;
      var marker := second & 127;
      PrefixValid(first, second) &&
      length <= limit && length <= 0x7fffffff && (length & 0x8000000000000000) == 0 &&
      (if marker < 126 then length == marker as bv64
       else if marker == 126 then 126 <= length <= 65535 else length >= 65536) &&
      (op < 8 || ((first & 128) != 0 && length <= 125))
    }

    // RFC 6455 section 5.2, with the library policy of HeaderValid. Decodes the header of a
    // frame the server sends: a header of 2, 4 or 10 bytes chosen by the 7-bit marker, the
    // extended length in network byte order, and status 2 exactly when HeaderValid holds.
    // Library policy: status 0, more header bytes needed, serves incremental reads.
    // Status: 0 need more header bytes, 1 reject, 2 accept. Size includes the two prefix bytes.
    static method DecodeHeader(first: bv8, second: bv8, extended: array<bv8>, available: Size, limit: bv64)
      returns (status: bv8, length: bv64, headerSize: Size)
      requires available as int <= extended.Length
      ensures status <= 2
      ensures headerSize == (if (second & 127) <= 125 then 2 else if (second & 127) == 126 then 4 else 10)
      ensures status == 0 <==> PrefixValid(first, second) && available < headerSize - 2
      ensures status == 2 ==> HeaderValid(first, second, length, limit) && available >= headerSize - 2
      ensures PrefixValid(first, second) && available >= headerSize - 2 ==>
        length == (if (second & 127) <= 125 then (second & 127) as bv64 else Word(extended[..headerSize as int - 2])) &&
        (status == 2 <==> HeaderValid(first, second, length, limit))
    {
      var marker := second & 127;
      headerSize := if marker <= 125 then 2 else if marker == 126 then 4 else 10;
      status, length := 1, 0;
      if !PrefixValid(first, second) { return; }
      if available < headerSize - 2 { status := 0; return; }
      if marker <= 125 { length := marker as bv64; }
      else { length := ReadLength(extended, headerSize - 2); }
      status := if HeaderValid(first, second, length, limit) then 2 else 1;
    }

    // RFC 6455 sections 5.4, 5.5 and 5.5.1, mixed with library policy. Section 5.4: a
    // message starts with opcode 0x1 or 0x2 and continues and ends with opcode 0x0, the
    // fragments of two messages are not interleaved, and control frames may arrive between
    // fragments. Section 5.5: a control frame has FIN set and a length of 125 bytes or less.
    // Library policy: the reassembled message size limit (section 10.4 asks for one), a
    // received Close discards any partial message, and state 8 rejects every later frame
    // (section 5.5.1 forbids only later data frames from the peer).
    // State: 0 idle, 1 text, 2 binary, 8 closed. Rejection leaves state/size unchanged.
    static method Step(state: bv8, op: bv8, fin: bool, total: Size, length: Size, limit: Size)
      returns (ok: bool, next: bv8, size: Size, emit: bool)
      requires state == 0 || state == 1 || state == 2 || state == 8
      requires total <= limit
      ensures !ok ==> next == state && size == total && !emit
      ensures ok ==> size <= limit && (next == 0 || next == 1 || next == 2 || next == 8)
      ensures ok && op == 8 ==> next == 8 && size == 0 && !emit
      ensures ok && (op == 9 || op == 10) ==> next == state && size == total && !emit
      ensures ok && op < 8 ==> size as int == (if state == 0 then 0 else total as int) + length as int
      ensures ok && op < 8 ==> emit == fin && next == (if fin then 0 else if state == 0 then op else state)
      ensures ok <==> state != 8 && Known(op) &&
        (if op >= 8 then fin && length <= 125 else
          (if state == 0 then op == 1 || op == 2 else op == 0) &&
          (if state == 0 then length <= limit else length as int <= limit as int - total as int))
    {
      ok, next, size, emit := false, state, total, false;
      if state == 8 || !Known(op) { return; }
      if op >= 8 {
        if !fin || length > 125 { return; }
        ok := true;
        if op == 8 { next, size := 8, 0; }
        return;
      }
      if (state == 0 && op != 1 && op != 2) || (state != 0 && op != 0) { return; }
      var prior: Size := if state == 0 then 0 else total;
      if length > limit - prior { return; }
      ok, size, emit := true, prior + length, fin;
      next := if fin then 0 else if state == 0 then op else state;
    }

    // RFC 6455 sections 5.2, 5.4 and 5.5, through DecodeHeader and Step, mixed with library
    // policy. Library policy: a frame is admitted or rejected from its header and the
    // message state alone, before any payload byte is read or allocated (section 10.4 asks
    // an implementation to protect itself against exceeding its limits).
    // Header admission includes message state BEFORE any payload allocation/read.
    static method {:isolate_assertions} Admit(first: bv8, second: bv8, extended: array<bv8>, available: Size,
      state: bv8, total: Size, limit: Size)
      returns (status: bv8, length: bv64, headerSize: Size, next: bv8, size: Size, emit: bool)
      requires available as int <= extended.Length
      requires state == 0 || state == 1 || state == 2 || state == 8
      requires total <= limit
      ensures status <= 2
      ensures status != 2 ==> next == state && size == total && !emit
      ensures status == 2 ==> HeaderValid(first, second, length, limit as bv64) && size <= limit
      ensures status == 2 ==> state != 8 &&
        (if (first & 15) < 8 then
          (if state == 0 then (first & 15) == 1 || (first & 15) == 2 else (first & 15) == 0) &&
          size as int == (if state == 0 then 0 else total as int) + length as int &&
          emit == ((first & 128) != 0)
         else !emit)
      ensures status == 2 && (first & 15) < 8 ==> next ==
        (if (first & 128) != 0 then 0 else if state == 0 then first & 15 else state)
      ensures status == 2 && (first & 15) == 8 ==> next == 8 && size == 0
      ensures status == 2 && ((first & 15) == 9 || (first & 15) == 10) ==> next == state && size == total
    {
      next, size, emit := state, total, false;
      status, length, headerSize := DecodeHeader(first, second, extended, available, limit as bv64);
      if status != 2 { return; }
      var ok: bool;
      ok, next, size, emit := Step(state, first & 15, (first & 128) != 0, total, length as Size, limit);
      if !ok { status := 1; }
    }

    // Library policy: the transmit state encoding. Its states follow RFC 6455 section 5.4
    // (a message in progress) and section 5.5.1 (Close sent); state 15, failed, is library
    // policy.
    // Transmit state is committed under the same lock as the wire write.
    // 0 idle, 1 text, 2 binary, 8 Close sent, 15 failed (only Close allowed).
    static function TxState(state: bv8): bool { state == 0 || state == 1 || state == 2 || state == 8 || state == 15 }
    // RFC 6455 sections 5.4, 5.5 and 5.5.1, mixed with library policy. Section 5.4: a
    // message starts with opcode 0x1 or 0x2 and continues with opcode 0x0, and control
    // frames may be sent between fragments. Section 5.5: a control frame MUST NOT be
    // fragmented, so FIN is set. Section 5.5.1: no data frame is sent after Close. Library
    // policy: after Close only a Pong with FIN set may follow (section 5.5.2 would also
    // allow a Ping), Close is not sent twice, and after a failed write (state 15) only
    // Close may follow.
    static method SendStep(state: bv8, op: bv8, fin: bool) returns (ok: bool, next: bv8)
      requires TxState(state)
      ensures TxState(next)
      ensures !ok ==> next == state
      ensures ok <==> Known(op) && (if state == 8 then op == 10 && fin else
        (if op == 8 then fin else state != 15 &&
          (if op >= 8 then fin else if state == 0 then op == 1 || op == 2 else op == 0)))
      ensures ok && op == 8 ==> next == 8
      ensures ok && (op == 9 || op == 10) ==> next == state
      ensures ok && op < 8 ==> next == (if fin then 0 else if state == 0 then op else state)
    {
      next := state;
      ok := Known(op) && (if state == 8 then op == 10 && fin else
        (if op == 8 then fin else state != 15 &&
          (if op >= 8 then fin else if state == 0 then op == 1 || op == 2 else op == 0)));
      if ok { next := if op == 8 then 8 else if op >= 8 then state else if fin then 0 else if state == 0 then op else state; }
    }

    // Library policy: after a failed or partial write the wire is in an unknown state, so
    // the sender moves to state 15, or stays at 8. No RFC 6455 section defines this.
    // Failure is irreversible for application data, including a partially written frame.
    static function SendFailed(state: bv8): bv8 { if state == 8 then 8 else 15 }

    // RFC 6455 sections 5.5.1 and 7.1.2, mixed with library policy. Sections 5.5.1 and
    // 7.1.2: once Close has been both sent and received, the TCP connection is to be closed,
    // which is the release decision. Library policy: the sequential Close state model, with
    // bits that are never cleared, Close sent at most once and never after a timeout or
    // transport failure, and release also on a timeout or transport failure.
    // Closing state bits: sent=1, received=2, terminal timeout/failure=4.
    // event: 0 request send, 1 receive Close, 2 timeout or transport failure.
    static method CloseStep(state: bv8, event: bv8) returns (next: bv8, send: bool, release: bool)
      requires state <= 7 && event <= 2
      ensures next <= 7 && (next & state) == state
      ensures send <==> event == 0 && (state & 5) == 0
      ensures send ==> (next & 1) == 1
      ensures (state & 1) != 0 ==> !send
      ensures release <==> (next & 4) != 0 || (next & 3) == 3
      ensures release ==> (next & 3) == 3 || event == 2 || (state & 4) != 0
      ensures event == 1 ==> (next & 2) != 0
    {
      send := event == 0 && (state & 5) == 0;
      next := if event == 0 then (if send then state | 1 else state)
              else if event == 1 then state | 2 else state | 4;
      release := (next & 4) != 0 || (next & 3) == 3;
    }

    // RFC 6455 section 5.4. A fragmented message's payload is the concatenation of the
    // fragments' payloads in order; Parts is that concatenation.
    static ghost function Parts(parts: array<array<bv8>>, n: nat): seq<bv8>
      requires n <= parts.Length
      reads parts, set i: int | 0 <= i < parts.Length :: parts[i]
    {
      if n == 0 then [] else Parts(parts, n - 1) + parts[n - 1][..]
    }

    // RFC 6455 section 5.4, mixed with library policy. Section 5.4: the fragments' payloads
    // are concatenated in order. Library policy: one exact-sized buffer, filled with the
    // trusted .NET bulk copy (NativeCopy.CopySlice).
    // Copies retained segments in order into one exact-sized message buffer.
    // Production merge uses the explicitly trusted .NET bulk-copy primitive.
    // Its orchestration has the same contract as the fully verified scalar reference.
    static method Merge(parts: array<array<bv8>>, total: Size) returns (result: array<bv8>)
      requires parts.Length <= 0x7fffffff
      requires |Parts(parts, parts.Length)| == total as int
      ensures fresh(result) && result[..] == Parts(parts, parts.Length)
    {
      result := new bv8[total];
      var offset: Size := 0;
      var p: Size := 0;
      var partCount := parts.Length as Size;
      while p < partCount
        invariant 0 <= p as int <= parts.Length
        invariant offset as int == |Parts(parts, p as int)| && offset <= total
        invariant result[..offset] == Parts(parts, p as int)
        invariant Parts(parts, parts.Length)[..offset] == Parts(parts, p as int)
      {
        PartsPrefix(parts, p as int, parts.Length);
        PartsPrefix(parts, p as int + 1, parts.Length);
        var segment := parts[p];
        NativeCopy.CopySlice(segment, result, offset);
        assert result[..offset as int+segment.Length] == Parts(parts, p as int) + segment[..];
        offset := offset + segment.Length as Size;
        p := p + 1;
      }
    }

    // RFC 6455 section 5.4, mixed with library policy. Section 5.4: the fragments' payloads
    // are concatenated in order. Library policy: the fully verified scalar reference for
    // Merge, with the same contract and no trusted copy.
    static method MergeScalar(parts: array<array<bv8>>, total: Size) returns (result: array<bv8>)
      requires parts.Length <= 0x7fffffff
      requires |Parts(parts, parts.Length)| == total as int
      ensures fresh(result) && result[..] == Parts(parts, parts.Length)
    {
      result := new bv8[total as int];
      var offset: Size := 0;
      var p: Size := 0;
      var partCount := parts.Length as Size;
      while p < partCount
        invariant 0 <= p as int <= parts.Length
        invariant offset as int == |Parts(parts, p as int)| && offset <= total
        invariant result[..offset] == Parts(parts, p as int)
        invariant Parts(parts, parts.Length)[..offset] == Parts(parts, p as int)
      {
        PartsPrefix(parts, p as int, parts.Length);
        PartsPrefix(parts, p as int + 1, parts.Length);
        var segment := parts[p];
        var segmentLength := segment.Length as Size;
        var j: Size := 0;
        while j < segmentLength
          invariant 0 <= j as int <= segment.Length
          invariant result[..offset] == Parts(parts, p as int)
          invariant result[offset..offset+j] == segment[..j]
        {
          result[offset+j] := segment[j];
          j := j + 1;
        }
        assert result[..offset as int+segment.Length] == Parts(parts, p as int) + segment[..];
        offset := offset + segment.Length as Size;
        p := p + 1;
      }
    }

    // RFC 6455 section 5.4, through Parts. A proof helper for Merge and MergeScalar; it
    // states no protocol rule of its own.
    static lemma PartsPrefix(parts: array<array<bv8>>, n: nat, end: nat)
      requires n <= end <= parts.Length
      ensures |Parts(parts, n)| <= |Parts(parts, end)|
      ensures Parts(parts, end)[..|Parts(parts, n)|] == Parts(parts, n)
      decreases end - n
    {
      if n < end { PartsPrefix(parts, n, end - 1); }
    }

    // RFC 6455 section 5.2, through Word. A proof helper for network byte order; it states
    // no protocol rule of its own.
    static lemma WordSnoc(prefix: seq<bv8>, last: bv8)
      ensures Word(prefix + [last]) == (Word(prefix) << 8) | (last as bv64)
    {
      assert (prefix + [last])[..|prefix|] == prefix;
    }

    // RFC 6455 section 5.2. The 2-byte and 8-byte network byte order forms of a 32-bit
    // value decode to its low 16 bits and to the value. A proof helper for HeaderRoundTrip.
    static lemma {:isolate_assertions} NetworkWord(value: bv32)
      ensures Word([(((value as bv64) >> 8) & 255) as bv8, (value & 255) as bv8]) == (value & 65535) as bv64
      ensures Word([0, 0, 0, 0, ((value >> 24) & 255) as bv8, ((value >> 16) & 255) as bv8,
        ((value >> 8) & 255) as bv8, (value & 255) as bv8]) == value as bv64
    {
      var a := ((value >> 24) & 255) as bv8;
      var b := ((value >> 16) & 255) as bv8;
      var c := ((value >> 8) & 255) as bv8;
      var d := (value & 255) as bv8;
      WordSnoc([], 0);
      WordSnoc([0], 0);
      WordSnoc([0, 0], 0);
      WordSnoc([0, 0, 0], 0);
      WordSnoc([0, 0, 0, 0], a);
      WordSnoc([0, 0, 0, 0, a], b);
      WordSnoc([0, 0, 0, 0, a, b], c);
      WordSnoc([0, 0, 0, 0, a, b, c], d);
      var z3: seq<bv8> := [0, 0, 0];
      var z4: seq<bv8> := [0, 0, 0, 0];
      var p5: seq<bv8> := [0, 0, 0, 0, a];
      var p6: seq<bv8> := [0, 0, 0, 0, a, b];
      var p7: seq<bv8> := [0, 0, 0, 0, a, b, c];
      assert z3 + [0] == z4;
      assert z4 + [a] == p5;
      assert p5 + [b] == p6;
      assert p6 + [c] == p7;
      assert p7 + [d] == [0, 0, 0, 0, a, b, c, d];
      assert Word([0, 0]) == 0;
      assert Word([0, 0, 0, 0]) == 0;
      assert Word([0, 0, 0, 0, a]) == a as bv64;
      assert Word([0, 0, 0, 0, a, b]) == ((a as bv64) << 8) | (b as bv64);
      assert Word([0, 0, 0, 0, a, b, c]) == ((a as bv64) << 16) | ((b as bv64) << 8) | (c as bv64);
      assert Word([0, 0, 0, 0, a, b, c, d]) == ((a as bv64) << 24) | ((b as bv64) << 16) | ((c as bv64) << 8) | (d as bv64);
    }

    // RFC 6455 sections 5.2 and 5.3, mixed with library policy. Section 5.2: for any length,
    // the header the encoder writes uses the minimal length encoding, and the decoder reads
    // the same length back. Section 5.3: the client header carries MASK and a 4-byte key,
    // which the server-header decoder does not read. Library policy: lengths up to
    // 0x7fffffff - 14, so that the whole frame fits a .NET array.
    // The encoder writes CLIENT headers. Strip only MASK before invoking the
    // SERVER decoder; masking keys are outside the decoded length field.
    static method {:isolate_assertions} {:timeLimit 120} HeaderRoundTrip(length: bv32) returns (decoded: bv64, serverHeader: Size)
      requires length <= 0x7fffffff - 14
      ensures decoded == length as bv64
      ensures serverHeader == (if length <= 125 then 2 else if length <= 65535 then 4 else 10)
    {
      var ok, first, header, total := Outgoing(2, 255, length as Size);
      assert ok;
      var buffer := new bv8[14];
      WriteHeader(buffer, first, length as bv32, header);
      var ext := new bv8[8];
      var count: Size := header - 6;
      var i: Size := 0;
      while i < count
        modifies ext
        invariant i <= count
        invariant forall j :: 0 <= j < i as int ==> ext[j] == buffer[2+j]
      { ext[i] := buffer[2+i]; i := i + 1; }
      var value := length;
      NetworkWord(value);
      if count == 2 {
        assert value <= 65535;
        assert (value & 65535) == value;
        assert ext[..2] == [(((value as bv64) >> 8) & 255) as bv8, (value & 255) as bv8];
        assert Word(ext[..2]) == value as bv64;
      } else if count == 8 {
        assert ext[..8] == [0, 0, 0, 0, ((value >> 24) & 255) as bv8,
          ((value >> 16) & 255) as bv8, ((value >> 8) & 255) as bv8, (value & 255) as bv8];
        assert Word(ext[..8]) == value as bv64;
      }
      assert first == 0x82;
      assert count == (if length <= 125 then 0 else if length <= 65535 then 2 else 8);
      assert PrefixValid(first, buffer[1] & 127);
      assert length > 125 ==> Word(ext[..count as int]) == length as bv64;
      assert HeaderValid(first, buffer[1] & 127, length as bv64, 0x7fffffff);
      var status: bv8;
      status, decoded, serverHeader := DecodeHeader(first, buffer[1] & 127, ext, count, 0x7fffffff);
      assert status == 2 && decoded == length as bv64 && serverHeader + 4 == header;
    }

    // RFC 6455 sections 7.4.1 and 7.4.2, mixed with library policy. Section 7.4.1: 1005,
    // 1006 and 1015 MUST NOT be set in a Close frame, and 1004 is reserved. Section 7.4.2:
    // 3000-3999 are for libraries, frameworks and applications, and 4000-4999 for private
    // use. Library policy: 1012-1014, registered with IANA but not defined in RFC 6455, are
    // accepted, and a received code outside the accepted set is rejected, which RFC 6455
    // does not require for 0-999, 1004 or 1016-2999.
    static function CloseCode(code: bv16): bool {
      (1000 <= code <= 1014 && code != 1004 && code != 1005 && code != 1006) || 3000 <= code <= 4999
    }
    // RFC 6455 sections 5.5, 5.5.1, 7.4 and 8.1. Section 5.5: a Close payload is 125 bytes
    // or less. Section 5.5.1: a body, if present, starts with a 2-byte status code (section
    // 7.4, see CloseCode) followed by a UTF-8 reason. Section 8.1: invalid UTF-8 fails the
    // connection. Erratum 7262 (status Reported) asks whether the reason must be UTF-8.
    static function CloseValid(length: Size, code: bv16, utf8: bool): bool {
      length <= 125 && (length == 0 || (length >= 2 && CloseCode(code) && utf8))
    }
    // RFC 6455 section 4.1, the client's checks of the server's response, mixed with library
    // policy. The caller computes each input: status 101 (check 1), Upgrade "websocket"
    // (check 2), Connection "Upgrade" (check 3), Sec-WebSocket-Accept (check 4), and a
    // subprotocol the client offered (check 6). Library policy: the client offers no
    // extensions, so any Sec-WebSocket-Extensions field fails (check 5), and the caller
    // requires HTTP/1.1. The proof covers the conjunction, not how the inputs are computed.
    static function HandshakeValid(status101: bool, upgrade: bool, connection: bool,
      accept: bool, protocol: bool, noExtensions: bool): bool {
      status101 && upgrade && connection && accept && protocol && noExtensions
    }

    // RFC 6455 sections 5.2, 5.3, 5.4 and 5.5, mixed with library policy. Section 5.2: a
    // header of 2, 4 or 10 bytes by the minimal length encoding, RSV bits 0 and a known
    // opcode; section 5.3 adds the 4-byte masking key, giving 6, 8 or 14. Section 5.4: FIN
    // and opcode follow the fragment's position, and continuation frames carry opcode 0.
    // Section 5.5: a control frame is not fragmented and is 125 bytes or less. Library
    // policy: the fragment argument (255 whole message, 0 first fragment, 128 last fragment,
    // and opcode 0 with 255 a middle fragment) and a frame total of at most 0x7fffffff.
    static method Outgoing(op: bv8, fragment: bv8, length: Size)
      returns (ok: bool, first: bv8, header: Size, total: Size)
      ensures ok ==> header == (if length <= 125 then 6 else if length <= 65535 then 8 else 14)
      ensures ok ==> total as int == header as int + length as int
      ensures ok ==> Known(first & 15) && (first & 112) == 0
      ensures ok && op >= 8 ==> first == (op | 128) && length <= 125
      ensures ok && op < 8 ==> first ==
        (if fragment == 128 then 128 else if op == 0 then 0 else if fragment == 0 then op else op | 128)
      ensures ok <==> Known(op) && (fragment == 0 || fragment == 128 || fragment == 255) &&
        (op < 8 || (fragment == 255 && length <= 125)) && !(op == 0 && fragment == 0) &&
        length as int + (if length <= 125 then 6 else if length <= 65535 then 8 else 14) <= 0x7fffffff
    {
      first, total := 0, 0;
      header := if length <= 125 then 6 else if length <= 65535 then 8 else 14;
      ok := Known(op) && (fragment == 0 || fragment == 128 || fragment == 255) &&
        (op < 8 || (fragment == 255 && length <= 125)) && !(op == 0 && fragment == 0) &&
        length <= 0x7fffffff - header;
      if !ok { return; }
      total := header + length;
      first := if op >= 8 then op | 128 else if fragment == 128 then 128
        else if op == 0 then 0 else if fragment == 0 then op else op | 128;
    }

    // RFC 6455 sections 5.2 and 5.3. Writes the first byte, then MASK set (every frame from
    // client to server is masked) with the 7-bit marker, then the 16-bit or 64-bit length in
    // network byte order by the minimal encoding. The 64-bit form's most significant bit is
    // 0 because the length is 32 bits. Bytes from the masking key onward are unchanged.
    static method {:isolate_assertions} WriteHeader(buffer: array<bv8>, first: bv8, length: bv32, header: Size)
      requires header == (if length <= 125 then 6 else if length <= 65535 then 8 else 14)
      requires buffer.Length >= header as int
      modifies buffer
      ensures buffer[0] == first
      ensures buffer[1] == (if length <= 125 then (length as bv8) | 128 else if length <= 65535 then 254 else 255)
      ensures 125 < length <= 65535 ==> buffer[2] == (((length as bv64) >> 8) & 255) as bv8 && buffer[3] == ((length as bv64) & 255) as bv8
      ensures length > 65535 ==> buffer[2] == (((length as bv64) >> 56) & 255) as bv8
      ensures length > 65535 ==> buffer[3] == (((length as bv64) >> 48) & 255) as bv8
      ensures length > 65535 ==> buffer[4] == (((length as bv64) >> 40) & 255) as bv8
      ensures length > 65535 ==> buffer[5] == (((length as bv64) >> 32) & 255) as bv8
      ensures length > 65535 ==> buffer[6] == (((length as bv64) >> 24) & 255) as bv8
      ensures length > 65535 ==> buffer[7] == (((length as bv64) >> 16) & 255) as bv8
      ensures length > 65535 ==> buffer[8] == (((length as bv64) >> 8) & 255) as bv8
      ensures length > 65535 ==> buffer[9] == (((length as bv64) >> 0) & 255) as bv8
      ensures forall j :: header as int - 4 <= j < buffer.Length ==> buffer[j] == old(buffer[j])
    {
      buffer[0] := first;
      if length <= 125 {
        buffer[1] := (length as bv8) | 128;
      } else if length <= 65535 {
        buffer[1] := 254;
        var v := length as bv64;
        buffer[2], buffer[3] := ((v >> 8) & 255) as bv8, (v & 255) as bv8;
      } else {
        buffer[1] := 255;
        var v := length as bv64;
        buffer[2], buffer[3], buffer[4], buffer[5] := ((v >> 56) & 255) as bv8, ((v >> 48) & 255) as bv8, ((v >> 40) & 255) as bv8, ((v >> 32) & 255) as bv8;
        buffer[6], buffer[7], buffer[8], buffer[9] := ((v >> 24) & 255) as bv8, ((v >> 16) & 255) as bv8, ((v >> 8) & 255) as bv8, (v & 255) as bv8;
      }
    }

    // RFC 6455 section 5.3. Output octet j is input octet j XOR masking key octet j MOD 4.
    // Bytes of dest outside the copied range are unchanged.
    static method {:isolate_assertions} CopyMasked(source: array<bv8>, dest: array<bv8>, offset: Size, count: Size,
      k0: bv8, k1: bv8, k2: bv8, k3: bv8)
      requires source != dest
      requires count as int <= source.Length
      requires offset as int + count as int <= dest.Length
      requires offset as int + count as int <= 0x7fffffff
      modifies dest
      ensures forall j :: 0 <= j < count as int ==> dest[offset as int + j] == source[j] ^
        (if j % 4 == 0 then k0 else if j % 4 == 1 then k1 else if j % 4 == 2 then k2 else k3)
      ensures forall j :: 0 <= j < dest.Length && !(offset as int <= j < offset as int + count as int) ==> dest[j] == old(dest[j])
    {
      var i: Size := 0;
      while count - i >= 4
        invariant i <= count && i % 4 == 0
        invariant forall j :: 0 <= j < i as int ==> dest[offset as int + j] == source[j] ^
          (if j % 4 == 0 then k0 else if j % 4 == 1 then k1 else if j % 4 == 2 then k2 else k3)
        invariant forall j :: 0 <= j < dest.Length && !(offset as int <= j < offset as int + i as int) ==> dest[j] == old(dest[j])
        decreases count - i
      {
        assert (i + 1) % 4 == 1;
        assert (i + 2) % 4 == 2;
        assert (i + 3) % 4 == 3;
        dest[offset + i] := source[i] ^ k0;
        dest[offset + i + 1] := source[i + 1] ^ k1;
        dest[offset + i + 2] := source[i + 2] ^ k2;
        dest[offset + i + 3] := source[i + 3] ^ k3;
        forall j | 0 <= j < i as int + 4
          ensures dest[offset as int + j] == source[j] ^
            (if j % 4 == 0 then k0 else if j % 4 == 1 then k1 else if j % 4 == 2 then k2 else k3)
        {
          if j >= i as int {
            if j == i as int { assert j % 4 == 0; }
            else if j == i as int + 1 { assert j % 4 == 1; }
            else if j == i as int + 2 { assert j % 4 == 2; }
            else { assert j == i as int + 3 && j % 4 == 3; }
          }
        }
        i := i + 4;
      }
      while i < count
        invariant i <= count
        invariant forall j :: 0 <= j < i as int ==> dest[offset as int + j] == source[j] ^
          (if j % 4 == 0 then k0 else if j % 4 == 1 then k1 else if j % 4 == 2 then k2 else k3)
        invariant forall j :: 0 <= j < dest.Length && !(offset as int <= j < offset as int + i as int) ==> dest[j] == old(dest[j])
        decreases count - i
      {
        var k := if i % 4 == 0 then k0 else if i % 4 == 1 then k1 else if i % 4 == 2 then k2 else k3;
        dest[offset + i] := source[i] ^ k;
        i := i + 1;
      }
    }

    // RFC 6455 section 5.3, mixed with library policy. Section 5.3: octet j is XORed with
    // masking key octet j MOD 4. Library policy: masking in place, with the key read from
    // the same buffer at least 4 bytes before the payload.
    static method MaskSlice(dest: array<bv8>, offset: Size, count: Size, keyOffset: Size)
      requires keyOffset as int + 4 <= offset as int
      requires offset as int + count as int <= dest.Length
      requires offset as int + count as int <= 0x7fffffff
      modifies dest
      ensures forall j :: 0 <= j < count as int ==> dest[offset as int + j] == old(dest[offset as int + j]) ^ old(dest[keyOffset as int + j % 4])
      ensures forall j :: 0 <= j < dest.Length && !(offset as int <= j < offset as int + count as int) ==> dest[j] == old(dest[j])
    {
      var i: Size := 0;
      while i < count
        invariant i <= count
        invariant forall j :: 0 <= j < i as int ==> dest[offset as int + j] == old(dest[offset as int + j]) ^ old(dest[keyOffset as int + j % 4])
        invariant forall j :: 0 <= j < dest.Length && !(offset as int <= j < offset as int + i as int) ==> dest[j] == old(dest[j])
        decreases count - i
      {
        dest[offset + i] := dest[offset + i] ^ dest[keyOffset + i % 4];
        i := i + 1;
      }
    }
  }
  // Library policy: a trusted external copy for Merge, not an RFC 6455 rule.
  // Trusted boundary: implemented ONLY by System.Buffer.BlockCopy in C#.
  // This declaration is an assumption about .NET, not a verified algorithm.
  class NativeCopy {
    static method {:extern} CopySlice(source: array<bv8>, dest: array<bv8>, offset: Size)
      requires source != dest
      requires offset as int + source.Length <= dest.Length
      modifies dest
      ensures dest[offset as int..offset as int + source.Length] == source[..]
      ensures forall j :: 0 <= j < dest.Length && !(offset as int <= j < offset as int + source.Length) ==> dest[j] == old(dest[j])
  }

}
