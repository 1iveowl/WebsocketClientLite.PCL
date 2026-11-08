using System;
using System.Linq;
using System.Text;
using WebsocketClientLite.VerifiedProtocol;
using Xunit;

namespace WebsocketClientLiteTest;

public class PayloadBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergeVariantsPreserveAliasedSegmentsAndReturnFreshStorage(bool scalar)
    {
        byte[] shared = { 4, 2 };
        byte[] tail = { 9 };
        byte[][] parts = { shared, Array.Empty<byte>(), shared, tail };
        var result = scalar ? Protocol.MergeScalar(parts, 5) : Protocol.Merge(parts, 5);
        Assert.Equal(new byte[] { 4, 2, 4, 2, 9 }, result);
        Assert.Equal(new byte[] { 4, 2 }, shared);
        Assert.Equal(new byte[] { 9 }, tail);
        Assert.Same(shared, parts[0]); Assert.Same(shared, parts[2]);
        Assert.All(parts, p => Assert.NotSame(p, result));
        result[0] = 255;
        Assert.Equal(new byte[] { 4, 2 }, shared);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    public void CopyMaskedUsesOnlyRequestedPrefixAndPreservesOtherBytes(int count)
    {
        var source = Encoding.ASCII.GetBytes("HelloMaskTAIL");
        var saved = source.ToArray();
        var dest = Enumerable.Repeat((byte)0xcd, count + 16).ToArray();
        var expected = dest.ToArray();
        // Independent fixed bytes; neither masking implementation computes the oracle.
        byte[] masked = { 0x7f, 0x9f, 0x4d, 0x51, 0x58, 0xb7, 0x40, 0x4e, 0x5c };
        Array.Copy(masked, 0, expected, 7, count);
        Protocol.CopyMasked(source, dest, 7, (uint)count, 0x37, 0xfa, 0x21, 0x3d);
        Assert.Equal(expected, dest);
        Assert.Equal(saved, source);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    public void MaskSliceMatchesKnownBytesAndPreservesEntireOutsideSlice(int count)
    {
        var dest = Enumerable.Repeat((byte)0xcd, 27).ToArray();
        Array.Copy(new byte[] { 0x37, 0xfa, 0x21, 0x3d }, 0, dest, 2, 4);
        Array.Copy(Encoding.ASCII.GetBytes("HelloMask"), 0, dest, 9, 9);
        var expected = dest.ToArray();
        byte[] masked = { 0x7f, 0x9f, 0x4d, 0x51, 0x58, 0xb7, 0x40, 0x4e, 0x5c };
        Array.Copy(masked, 0, expected, 9, count);
        Protocol.MaskSlice(dest, 9, (uint)count, 2);
        Assert.Equal(expected, dest);
    }
}
