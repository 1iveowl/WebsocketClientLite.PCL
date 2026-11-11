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
    public async Task Reassembles_MaskedFragmentedMessage()
    {
        // Two masked fragments, each with its own key, exercising unmask-in-place
        // plus reassembly: Text(FIN=0) "abc" + Continuation(FIN=1) "def".
        byte[] k1 = { 1, 2, 3, 4 };
        byte[] k2 = { 5, 6, 7, 8 };
        var e1 = "abc".Select((c, i) => (byte)((byte)c ^ k1[i % 4]));
        var e2 = "def".Select((c, i) => (byte)((byte)c ^ k2[i % 4]));

        var bytes = new List<byte> { 0x01, 0x80 | 3 };
        bytes.AddRange(k1);
        bytes.AddRange(e1);
        bytes.AddRange(new byte[] { 0x80, 0x80 | 3 });
        bytes.AddRange(k2);
        bytes.AddRange(e2);

        var fake = new FakeTcpConnectionService(bytes.ToArray());
        using var parser = new WebsocketParserHandler(fake);

        var df = await parser.DataframeObservable().FirstAsync();

        Assert.NotNull(df);
        Assert.Equal("abcdef", df!.Message);
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
        var frame = new byte[] { 0x82, 0xFE, 0x00, 0xC8 };

        await Assert.ThrowsAsync<WebsocketClientLiteException>(
            () => Parse(frame, maxFrameSize: 100));
    }

    [Fact]
    public async Task Parse_OversizedControlFrame_Throws()
    {
        // Ping (control) frame declaring a 200-byte payload (> 125 is illegal).
        var frame = new byte[] { 0x89, 0xFE, 0x00, 0xC8 };

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
    [InlineData(10)]
    [InlineData(125)]
    public async Task Parse_8Bit_Length(byte length)
    {
        byte opcodeFin = 0x81;
        byte maskLen = (byte)(0x80 | length);
        byte[] mask = { 1, 2, 3, 4 };
        byte[] payload = Enumerable.Repeat((byte)'a', length).ToArray();
        byte[] encoded = new byte[length];
        for (int i = 0; i < length; i++) encoded[i] = (byte)(payload[i] ^ mask[i % 4]);

        var frame = new byte[2 + 4 + length];
        frame[0] = opcodeFin; frame[1] = maskLen;
        Array.Copy(mask, 0, frame, 2, 4);
        Array.Copy(encoded, 0, frame, 6, length);

        var df = await Parse(frame);
        Assert.NotNull(df);
        Assert.Equal((ulong)length, df!.Length);
        Assert.Equal(new string('a', length), df.Message);
    }

    [Fact]
    public async Task Parse_16Bit_Length()
    {
        ushort length = 300;
        byte opcodeFin = 0x81;
        byte firstLen = 0xFE; // 126 + mask bit
        byte[] mask = { 1, 2, 3, 4 };
        byte[] payload = Enumerable.Repeat((byte)'b', length).ToArray();
        byte[] encoded = new byte[length];
        for (int i = 0; i < length; i++) encoded[i] = (byte)(payload[i] ^ mask[i % 4]);

        var frame = new byte[2 + 2 + 4 + length];
        frame[0] = opcodeFin; frame[1] = firstLen;
        frame[2] = (byte)(length >> 8); frame[3] = (byte)(length & 0xFF);
        Array.Copy(mask, 0, frame, 4, 4);
        Array.Copy(encoded, 0, frame, 8, length);

        var df = await Parse(frame);
        Assert.NotNull(df);
        Assert.Equal((ulong)length, df!.Length);
        Assert.Equal(new string('b', length), df.Message);
    }

    [Fact]
    public async Task Parse_64Bit_Length()
    {
        ulong length = 70000;
        byte opcodeFin = 0x81;
        byte firstLen = 0xFF; // 127 + mask bit
        byte[] mask = { 1, 2, 3, 4 };
        byte[] payload = Enumerable.Repeat((byte)'c', (int)length).ToArray();
        byte[] encoded = new byte[length];
        for (int i = 0; i < (int)length; i++) encoded[i] = (byte)(payload[i] ^ mask[i % 4]);

        var frame = new byte[2 + 8 + 4 + length];
        frame[0] = opcodeFin; frame[1] = firstLen;
        ulong l = length;
        frame[2] = (byte)(l >> 56); frame[3] = (byte)(l >> 48); frame[4] = (byte)(l >> 40); frame[5] = (byte)(l >> 32);
        frame[6] = (byte)(l >> 24); frame[7] = (byte)(l >> 16); frame[8] = (byte)(l >> 8); frame[9] = (byte)(l & 0xFF);
        Array.Copy(mask, 0, frame, 10, 4); Array.Copy(encoded, 0, frame, 14, (int)length);

        var df = await Parse(frame);
        Assert.NotNull(df);
        Assert.Equal(length, df!.Length);
        Assert.Equal(new string('c', (int)length), df.Message);
    }
}
