include "Protocol.dfy"
module {:extern "WebsocketClientLite.Verified"} MaskingCore {
  newtype {:nativeType "uint"} Index = n: int | 0 <= n <= 0x7fffffff

  class Masking {
    // RFC 6455 section 5.3. The key may occupy a larger scratch buffer.
    static method Apply(data: array<bv8>, key: array<bv8>, count: Index)
      requires data != key
      requires key.Length >= 4
      requires count as int == data.Length
      modifies data
      ensures forall j :: 0 <= j < data.Length ==>
        data[j] == old(data[j]) ^ key[j % 4]
    {
      var i: Index := 0;
      while i < count
        invariant 0 <= i <= count
        invariant forall j :: 0 <= j < i as int ==>
          data[j] == old(data[j]) ^ key[j % 4]
        invariant forall j :: i as int <= j < data.Length ==> data[j] == old(data[j])
        decreases count - i
      {
        data[i] := data[i] ^ key[i % 4];
        i := i + 1;
      }
    }

    static lemma XorTwice(value: bv8, key: bv8)
      ensures (value ^ key) ^ key == value
    {}

    static lemma RoundTrip(data: seq<bv8>, key: seq<bv8>)
      requires |key| >= 4
      ensures forall j :: 0 <= j < |data| ==>
        (data[j] ^ key[j % 4]) ^ key[j % 4] == data[j]
    {
      forall j | 0 <= j < |data| {
        XorTwice(data[j], key[j % 4]);
      }
    }
  }
}
