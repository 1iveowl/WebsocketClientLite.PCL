module {:extern "WebsocketClientLite.VerifiedProtocol"} ProtocolCore {
  newtype {:nativeType "uint"} Size = n: int | 0 <= n <= 0x7fffffff
  class Protocol {
    static ghost function Word(s: seq<bv8>): bv64 {
      if |s| == 0 then 0 else (Word(s[..|s|-1]) << 8) | (s[|s|-1] as bv64)
    }

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

    static function Known(op: bv8): bool { op == 0 || op == 1 || op == 2 || op == 8 || op == 9 || op == 10 }
    static function PrefixValid(first: bv8, second: bv8): bool {
      var op := first & 15;
      (first & 112) == 0 && (second & 128) == 0 && Known(op) &&
      (op < 8 || ((first & 128) != 0 && (second & 127) <= 125))
    }
    static function HeaderValid(first: bv8, second: bv8, length: bv64, limit: bv64): bool {
      var op := first & 15;
      var marker := second & 127;
      PrefixValid(first, second) &&
      length <= limit && length <= 0x7fffffff && (length & 0x8000000000000000) == 0 &&
      (if marker < 126 then length == marker as bv64
       else if marker == 126 then 126 <= length <= 65535 else length >= 65536) &&
      (op < 8 || ((first & 128) != 0 && length <= 125))
    }

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

    static function CloseCode(code: bv16): bool {
      (1000 <= code <= 1014 && code != 1004 && code != 1005 && code != 1006) || 3000 <= code <= 4999
    }
    static function CloseValid(length: Size, code: bv16, utf8: bool): bool {
      length <= 125 && (length == 0 || (length >= 2 && CloseCode(code) && utf8))
    }
    static function HandshakeValid(status101: bool, upgrade: bool, connection: bool,
      accept: bool, protocol: bool, noExtensions: bool): bool {
      status101 && upgrade && connection && accept && protocol && noExtensions
    }

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
}
