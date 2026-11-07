using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;
using WebsocketClientLite.Helper;
using WebsocketClientLite.CustomException;
using Xunit;
using System.Reflection;

namespace WebsocketClientLiteTest;

public class PayloadParsingTests
{
    private class FakeTcpConnectionService : TcpConnectionService
    {
        public FakeTcpConnectionService(byte[] bytes, int maxFrameSize = 0) : base(
            () => false,
            (o, c, ch, e) => true,
            (client, uri) => Task.CompletedTask,
            (status, ex) => { },
            true,
            new System.Net.Sockets.TcpClient(),
            maxFrameSize)
        {
            var streamField = typeof(TcpConnectionService)
                .GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic);
            streamField!.SetValue(this, new MemoryStream(bytes));
        }
    }

    private async Task<Dataframe?> Parse(byte[] frame, int maxFrameSize = 0)
    {
        var fake = new FakeTcpConnectionService(frame, maxFrameSize);
        return await fake.ReadDataframeAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Parse_UnmaskedTextFrame()
    {
        // FIN + Text, unmasked (server -> client), 7-bit length 5 "world".
        byte[] frame = { 0x81, 0x05, (byte)'w', (byte)'o', (byte)'r', (byte)'l', (byte)'d' };

        var df = await Parse(frame);

        Assert.NotNull(df);
        Assert.False(df!.MASK);
        Assert.Equal(5UL, df.Length);
        Assert.Equal("world", df.Message);
    }

    [Fact]
    public async Task Reassembles_FragmentedTextMessage()
    {
        // Unmasked fragments: Text(FIN=0) + Continuation(FIN=0) + Continuation(FIN=1).
        byte[] frames =
        {
            0x01, 0x03, (byte)'a', (byte)'b', (byte)'c', // Text, not final
            0x00, 0x03, (byte)'d', (byte)'e', (byte)'f', // Continuation, not final
            0x80, 0x03, (byte)'g', (byte)'h', (byte)'i', // Continuation, final
        };

        var fake = new FakeTcpConnectionService(frames);
        using var parser = new WebsocketParserHandler(fake);

        var df = await parser.DataframeObservable().FirstAsync();

        Assert.NotNull(df);
        Assert.Equal(OpcodeKind.Text, df!.Opcode);
        Assert.Equal("abcdefghi", df.Message);
    }

    [Fact]
    public async Task DataframeObservable_EmitsMultipleMessages_FromOneSubscription()
    {
        // Two complete unmasked text frames back-to-back; a single subscription
        // must yield both (the reader loops rather than re-subscribing per message).
        byte[] frames =
        {
            0x81, 0x03, (byte)'o', (byte)'n', (byte)'e',
            0x81, 0x03, (byte)'t', (byte)'w', (byte)'o',
        };

        var fake = new FakeTcpConnectionService(frames);
        using var parser = new WebsocketParserHandler(fake);

        var messages = await parser.DataframeObservable()
            .Take(2)
            .Select(df => df!.Message)
            .ToArray();

        Assert.Equal(new[] { "one", "two" }, messages);
    }

    [Fact]
    public async Task RejectsMaskedServerFrame()
    {
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => Parse(new byte[] { 0x81, 0x80, 1, 2, 3, 4 }));
    }

    [Fact]
    public async Task CloseFrame_StopsReading_AndCompletes()
    {
        // Complete text frame, then a Close frame, then trailing garbage that must
        // never be parsed (the reader stops after Close instead of hitting EOF).
        byte[] frames =
        {
            0x81, 0x02, (byte)'h', (byte)'i',
            0x88, 0x02, 0x03, 0xE8,   // Close, status 1000
            0xFF, 0xFF, 0xFF,         // garbage past the close
        };

        var fake = new FakeTcpConnectionService(frames);
        using var parser = new WebsocketParserHandler(fake);

        var emitted = await parser.DataframeObservable().ToList();

        Assert.Equal(2, emitted.Count);
        Assert.Equal("hi", emitted[0]!.Message);
        Assert.Equal(OpcodeKind.Close, emitted[1]!.Opcode);
    }

    [Fact]
    public async Task CloseFrame_DuringReassembly_AbortsFragmentedMessage()
    {
        // Text(FIN=0) starts a message; a Close arrives before any continuation.
        byte[] frames =
        {
            0x01, 0x02, (byte)'a', (byte)'b', // first fragment, not final
            0x88, 0x00,                        // Close, no payload
        };

        var fake = new FakeTcpConnectionService(frames);
        using var parser = new WebsocketParserHandler(fake);

        var emitted = await parser.DataframeObservable().ToList();

        // Only the Close frame surfaces; the incomplete message is dropped.
        var frame = Assert.Single(emitted);
        Assert.Equal(OpcodeKind.Close, frame!.Opcode);
    }

    [Fact]
    public async Task Parse_BinaryFrame_ExposesBinaryNotMessage()
    {
        byte[] payload = { 10, 20, 30, 40 };
        var frame = new byte[2 + payload.Length];
        frame[0] = 0x82;                  // FIN + Binary
        frame[1] = (byte)payload.Length;  // unmasked
        Array.Copy(payload, 0, frame, 2, payload.Length);

        var df = await Parse(frame);

        Assert.NotNull(df);
        Assert.Equal(OpcodeKind.Binary, df!.Opcode);
        Assert.Equal(payload, df.Binary);
        Assert.Null(df.Message); // Message is only produced for Text frames
    }

    [Fact]
    public async Task Parse_FrameExceedingMaxFrameSize_Throws()
    {
        // Binary frame, masked, 16-bit length declaring 200 bytes.
        var frame = new byte[] { 0x82, 0x7E, 0x00, 0xC8 };

        await Assert.ThrowsAsync<WebsocketClientLiteException>(
            () => Parse(frame, maxFrameSize: 100));
    }

    [Fact]
    public async Task Parse_OversizedControlFrame_Throws()
    {
        // Ping (control) frame declaring a 200-byte payload (> 125 is illegal).
        var frame = new byte[] { 0x89, 0x7E, 0x00, 0xC8 };

        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => Parse(frame));
    }

    [Fact]
    public async Task Parse_FragmentedControlFrame_Throws()
    {
        // Ping (control) frame with FIN = 0 (control frames must not be fragmented).
        var frame = new byte[] { 0x09, 0x00 };

        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => Parse(frame));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(300)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(70000)]
    public async Task ParsesCanonicalServerLengths(int length)
    {
        var frame = new List<byte> { 0x81 };
        if (length <= 125) frame.Add((byte)length);
        else if (length <= 65535) { frame.Add(126); frame.Add((byte)(length >> 8)); frame.Add((byte)length); }
        else { frame.Add(127); for (int shift = 56; shift >= 0; shift -= 8) frame.Add((byte)((ulong)length >> shift)); }
        frame.AddRange(Enumerable.Repeat((byte)'a', length));
        var parsed = await Parse(frame.ToArray());
        Assert.Equal((ulong)length, parsed!.Length);
        Assert.Equal(new string('a', length), parsed.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0xC1, 0 })]
    [InlineData(new byte[] { 0x83, 0 })]
    [InlineData(new byte[] { 0x81, 126, 0, 125 })]
    [InlineData(new byte[] { 0x81, 127, 0, 0, 0, 0, 0, 0, 255, 255 })]
    [InlineData(new byte[] { 0x81, 127, 128, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x88, 1, 0 })]
    [InlineData(new byte[] { 0x88, 2, 3, 237 })] // forbidden 1005
    [InlineData(new byte[] { 0x88, 3, 3, 232, 255 })] // invalid reason UTF-8
    public async Task RejectsMalformedFrames(byte[] bytes) =>
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => Parse(bytes));

    [Theory]
    [InlineData(new byte[] { 0x80, 0 })] // orphan continuation
    [InlineData(new byte[] { 0x01, 0, 0x81, 0 })] // new data during fragments
    [InlineData(new byte[] { 0x01, 0, 0x82, 0 })]
    [InlineData(new byte[] { 0x81, 1, 255 })] // invalid text
    public async Task RejectsInvalidMessageSequence(byte[] bytes)
    {
        using var fake = new FakeTcpConnectionService(bytes);
        using var parser = new WebsocketParserHandler(fake);
        await Assert.ThrowsAsync<WebsocketClientLiteException>(async () => await parser.DataframeObservable().FirstAsync());
    }

    [Fact]
    public async Task ReassemblyPreservesUtf8AcrossFragmentsAndInterleavedPing()
    {
        using var fake = new FakeTcpConnectionService(new byte[] { 0x01, 1, 0xe2, 0x89, 1, 42, 0x80, 2, 0x82, 0xac });
        using var parser = new WebsocketParserHandler(fake);
        var messages = await parser.DataframeObservable().Take(2).ToArray();
        Assert.Equal(OpcodeKind.Ping, messages[0]!.Opcode);
        Assert.Equal("€", messages[1]!.Message);
        Assert.Equal(3UL, messages[1]!.Length);
        Assert.Equal(FragmentKind.None, messages[1]!.Fragment);
    }

    [Fact]
    public async Task RejectsMessageAboveLimit()
    {
        using var fake = new FakeTcpConnectionService(new byte[] { 0x01, 2, 97, 98, 0x80, 2, 99, 100 }, 3);
        using var parser = new WebsocketParserHandler(fake);
        await Assert.ThrowsAsync<WebsocketClientLiteException>(async () => await parser.DataframeObservable().FirstAsync());
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 125 })] // orphan, body deliberately absent
    [InlineData(new byte[] { 0x01, 1, 97, 0x82, 125 })] // new data during a message
    [InlineData(new byte[] { 0x01, 2, 97, 98, 0x80, 2 })] // exceeds remaining budget
    public async Task RejectsSequenceBeforeAttemptingPayloadRead(byte[] bytes)
    {
        using var fake = new FakeTcpConnectionService(bytes, bytes.Length == 6 ? 3 : 125);
        using var parser = new WebsocketParserHandler(fake);
        var error = await Assert.ThrowsAsync<WebsocketClientLiteException>(
            async () => await parser.DataframeObservable().FirstAsync());
        // EOF would prove the parser tried to read the absent payload. Admission
        // must fail from the header and previous state instead.
        Assert.Contains("fragmentation sequence", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("aborted unexpectedly", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TruncatedHeadersNeverProduceFrames()
    {
        byte[] header = { 0x82, 127, 0, 0, 0, 0, 0, 1, 0, 0 };
        for (int length = 0; length < header.Length; length++)
            await Assert.ThrowsAsync<WebsocketClientLiteException>(() => Parse(header.Take(length).ToArray()));
    }
}
