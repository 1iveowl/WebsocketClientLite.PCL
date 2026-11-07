using System;

namespace WebsocketClientLite.VerifiedProtocol;

// Explicit trusted primitive for Protocol.Merge, specified in Protocol.dfy.
// Dafny proves call bounds, non-aliasing and concatenation; the .NET runtime's
// Buffer.BlockCopy semantics are assumed and independently integration-tested.
internal partial class NativeCopy
{
    public static void CopySlice(byte[] source, byte[] dest, uint offset) =>
        Buffer.BlockCopy(source, 0, dest, checked((int)offset), source.Length);
}
