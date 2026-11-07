using System;
using System.Linq;
using WebsocketClientLite.VerifiedProtocol;
using Xunit;

namespace WebsocketClientLiteTest;

public class ProtocolCoreTests
{
    [Fact]
    public void PartialHeadersReturnNeedMoreWithoutReadingUnavailableBytes()
    {
        byte[] extended = { 0, 0, 0, 0, 0, 1, 0, 0 };
        for (uint available = 0; available <= 8; available++)
        {
            Protocol.DecodeHeader(0x82, 127, extended.Take((int)available).ToArray(), available, 70000,
                out byte status, out ulong length, out uint headerSize);
            Assert.Equal(10U, headerSize);
            Assert.Equal(available < 8 ? 0 : 2, status);
            if (status == 2) Assert.Equal(65536UL, length);
        }
        Protocol.DecodeHeader(0x82, 255, Array.Empty<byte>(), 0, 70000, out byte maskedStatus, out _, out _);
        Assert.Equal(1, maskedStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(int.MaxValue - 14)]
    [InlineData(int.MaxValue - 13)]
    [InlineData(int.MaxValue)]
    public void OutgoingChecksBoundsWithoutHugeAllocations(int length)
    {
        Protocol.Outgoing(2, 255, (uint)length, out bool ok, out byte first, out uint header, out uint total);
        Assert.Equal(length <= int.MaxValue - 14 || length <= 65535, ok);
        if (!ok) return;
        Assert.Equal(0x82, first);
        Assert.Equal((ulong)length + header, total);
        var bytes = Enumerable.Repeat((byte)0xcc, 18).ToArray();
        Protocol.WriteHeader(bytes, first, (uint)length, header);
        Assert.Equal(0x82, bytes[0]);
        ulong decoded = 0;
        if (length <= 125) decoded = (ulong)(bytes[1] & 127);
        else for (int i = 2; i < header - 4; i++) decoded = decoded * 256 + bytes[i];
        Assert.Equal((ulong)length, decoded);
        Assert.All(bytes.Skip((int)header - 4), b => Assert.Equal(0xcc, b));
    }

    [Fact]
    public void TransitionMatrixRejectsIllegalStatesAndPreservesControls()
    {
        foreach (byte state in new byte[] { 0, 1, 2, 8 })
        for (byte opcode = 0; opcode < 16; opcode++)
        foreach (bool fin in new[] { false, true })
        {
            Protocol.Step(state, opcode, fin, 4, 2, 8, out bool ok, out byte next, out uint size, out bool emit);
            bool control = opcode is 8 or 9 or 10;
            bool data = state == 0 ? opcode is 1 or 2 : opcode == 0;
            Assert.Equal(state != 8 && (control ? fin : data), ok);
            if (!ok) { Assert.Equal(state, next); Assert.Equal(4U, size); Assert.False(emit); }
            else if (opcode is 9 or 10) { Assert.Equal(state, next); Assert.Equal(4U, size); Assert.False(emit); }
            else if (opcode == 8) { Assert.Equal(8, next); Assert.Equal(0U, size); Assert.False(emit); }
            else { Assert.Equal(fin, emit); Assert.Equal(state == 0 ? 2U : 6U, size); }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(257)]
    public void SliceMaskingPreservesPrefixSuffixAndSource(int count)
    {
        var source = Enumerable.Range(0, count).Select(i => (byte)i).ToArray();
        var original = source.ToArray();
        byte[] key = { 37, 250, 0, 255 };
        var dest = Enumerable.Repeat((byte)0xcd, count + 23).ToArray();
        Protocol.CopyMasked(source, dest, 7, (uint)count, key[0], key[1], key[2], key[3]);
        Assert.Equal(original, source);
        Assert.All(dest.Take(7).Concat(dest.Skip(7 + count)), b => Assert.Equal(0xcd, b));
        Assert.Equal(source.Select((b, i) => (byte)(b ^ key[i % 4])), dest.Skip(7).Take(count));
        Array.Copy(key, dest, 4);
        Protocol.MaskSlice(dest, 7, (uint)count, 0);
        Assert.Equal(key, dest.Take(4));
        Assert.Equal(source, dest.Skip(7).Take(count));
    }

    [Fact]
    public void HandshakeRequiresEveryCondition()
    {
        for (int flags = 0; flags < 64; flags++)
            Assert.Equal(flags == 63, Protocol.HandshakeValid((flags & 1) != 0, (flags & 2) != 0,
                (flags & 4) != 0, (flags & 8) != 0, (flags & 16) != 0, (flags & 32) != 0));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(1004, false)]
    [InlineData(1005, false)]
    [InlineData(1006, false)]
    [InlineData(1015, false)]
    [InlineData(2999, false)]
    [InlineData(3000, true)]
    [InlineData(4999, true)]
    [InlineData(5000, false)]
    public void CloseCodeRanges(ushort code, bool accepted) => Assert.Equal(accepted, Protocol.CloseValid(2, code, true));
}
