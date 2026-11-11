using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;
using Xunit;

namespace WebsocketClientLiteTest;

// S4 replay reduction for a779602, which has no ParserRetentionTests, no MissingPayload and
// no TcpConnectionService.AbortedUnexpectedlyMessage. Input, Connection, Join and Message are
// copied from ParserRetentionTests at bcdcda6, MissingPayload from PayloadParsingTests at
// bcdcda6, and Parse is PayloadParsingTests.Parse over the same MemoryStream. The four tests,
// their rows and their statements are verbatim from bcdcda6. Comments are left out, and the
// end-of-stream message is the literal text a779602 throws.
public class ParserRetentionTests
{
    private const string AbortedUnexpectedlyMessage =
        "Websocket connection aborted unexpectedly. Check connection and socket security version/TLS version).";
    private sealed class Input(byte[] bytes, bool cancelAtEnd = false) : MemoryStream(bytes)
    {
        public int Attempts { get; private set; }
        public int BytesRead => (int)Position;
        private int Next(Span<byte> destination)
        {
            Attempts++;
            if (Position == Length && cancelAtEnd) throw new OperationCanceledException();
            return base.Read(destination[..Math.Min(1, destination.Length)]);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            Task.FromResult(Next(buffer.AsSpan(offset, count)));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            new(Next(buffer.Span));
    }
    private static TcpConnectionService Connection(Stream stream, int limit = 0)
    {
        var connection = new TcpConnectionService(() => false, (_, _, _, _) => true,
            (_, _) => Task.CompletedTask, (_, _) => { }, true, maxFrameSize: limit);
        typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connection, stream);
        return connection;
    }
    private static async Task<Dataframe?> Parse(byte[] frame, int maxFrameSize = 0)
    {
        using var connection = Connection(new MemoryStream(frame), maxFrameSize);
        return await connection.ReadDataframeAsync(CancellationToken.None);
    }
    private static byte[] Join(params byte[][] pieces) => pieces.SelectMany(p => p).ToArray();
    private static void Message(Dataframe frame, byte opcode, byte[] expected)
    {
        Assert.True(frame.Payload!.SequenceEqual(expected), "Retained bytes differ from independent concatenation");
        Assert.True(frame.Length == (ulong)expected.Length, "Retained Length differs from byte total");
        Assert.True(frame.FIN && frame.Fragment == FragmentKind.None && (byte)frame.Opcode == opcode,
            "Original message metadata was not preserved");
    }
    private static int MissingPayload(byte[] bytes)
    {
        int marker = bytes[1] & 127;
        int headerSize = marker <= 125 ? 2 : marker == 126 ? 4 : 10;
        if (bytes.Length < headerSize) return 0;
        ulong announced = marker <= 125 ? (ulong)marker : 0;
        for (int i = 2; i < headerSize; i++) announced = announced << 8 | bytes[i];
        if (announced > 65536) return 0;
        return Math.Max(0, (int)announced - (bytes.Length - headerSize));
    }

    [Theory]
    [InlineData(new byte[] { 0xC1, 0 })] // RSV1
    [InlineData(new byte[] { 0xA1, 0 })] // RSV2
    [InlineData(new byte[] { 0x91, 0 })] // RSV3
    [InlineData(new byte[] { 0x83, 0 })] // reserved data opcodes 3 to 7
    [InlineData(new byte[] { 0x84, 0 })]
    [InlineData(new byte[] { 0x85, 0 })]
    [InlineData(new byte[] { 0x86, 0 })]
    [InlineData(new byte[] { 0x87, 0 })]
    [InlineData(new byte[] { 0x8B, 0 })] // reserved control opcodes 11 to 15
    [InlineData(new byte[] { 0x8C, 0 })]
    [InlineData(new byte[] { 0x8D, 0 })]
    [InlineData(new byte[] { 0x8E, 0 })]
    [InlineData(new byte[] { 0x8F, 0 })]
    [InlineData(new byte[] { 0x81, 126, 0, 125 })]
    [InlineData(new byte[] { 0x81, 127, 0, 0, 0, 0, 0, 0, 255, 255 })]
    [InlineData(new byte[] { 0x81, 127, 128, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x88, 1, 0 })]
    [InlineData(new byte[] { 0x88, 2, 3, 237 })] // forbidden 1005
    [InlineData(new byte[] { 0x88, 3, 3, 232, 255 })] // invalid reason UTF-8
    public async Task RejectsMalformedFrames(byte[] bytes)
    {
        var frame = bytes.Concat(new byte[MissingPayload(bytes)]).ToArray();
        var error = await Assert.ThrowsAsync<WebsocketClientLiteException>(() => Parse(frame));
        Assert.DoesNotContain(AbortedUnexpectedlyMessage, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 0x82, 126, 1, 0 })]
    [InlineData(new byte[] { 0x82, 127, 0, 0, 0, 0, 0, 1, 0, 0 })]
    public async Task TruncatedHeadersNeverProduceFrames(byte[] header)
    {
        var wrong = new List<string>();
        for (int length = 0; length < header.Length; length++)
        {
            try
            {
                var frame = await Parse(header.Take(length).ToArray());
                wrong.Add($"{length}: {(frame is null ? "null" : "frame")}");
            }
            catch (WebsocketClientLiteException error) when (error.Message != AbortedUnexpectedlyMessage)
            {
                wrong.Add($"{length}: {error.Message}");
            }
            catch (WebsocketClientLiteException)
            {
            }
        }
        Assert.True(wrong.Count == 0, "Truncated header did not end in end of stream at " + string.Join("; ", wrong));
    }

    [Theory]
    [InlineData(new byte[] { 0xC1, 1 }, 0, 2)] // RSV1
    [InlineData(new byte[] { 0xA1, 1 }, 0, 2)] // RSV2
    [InlineData(new byte[] { 0x91, 1 }, 0, 2)] // RSV3
    [InlineData(new byte[] { 0x83, 1 }, 0, 2)] // reserved data opcodes 3 to 7
    [InlineData(new byte[] { 0x84, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x85, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x86, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x87, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x8B, 1 }, 0, 2)] // reserved control opcodes 11 to 15
    [InlineData(new byte[] { 0x8C, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x8D, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x8E, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x8F, 1 }, 0, 2)]
    [InlineData(new byte[] { 0x81, 0x80, 1, 2, 3, 4 }, 0, 2)] // masked server frame
    [InlineData(new byte[] { 0x09, 1 }, 0, 2)] // fragmented control frame
    [InlineData(new byte[] { 0x89, 126, 0, 200 }, 0, 2)] // control length above 125
    [InlineData(new byte[] { 0x81, 126, 0, 125 }, 0, 4)] // non-minimal 16-bit length
    [InlineData(new byte[] { 0x82, 126, 0, 200 }, 100, 4)] // above the configured limit
    [InlineData(new byte[] { 0x81, 127, 0, 0, 0, 0, 0, 0, 255, 255 }, 0, 10)] // non-minimal 64-bit length
    [InlineData(new byte[] { 0x81, 127, 128, 0, 0, 0, 0, 0, 0, 0 }, 0, 10)] // bit 63, not padded
    public async Task InvalidHeadersRejectBeforePayloadRead(byte[] header, int limit, int read)
    {
        using var input = new Input(header.Concat(new byte[MissingPayload(header)]).ToArray());
        using var connection = Connection(input, limit);
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => connection.ReadDataframeAsync(CancellationToken.None));
        Assert.True(input.BytesRead == read && input.Attempts == read,
            $"Invalid header read {input.BytesRead} bytes in {input.Attempts} attempts, expected {read}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(200)]
    [InlineData(65535)]
    [InlineData(65536)]
    public async Task ConfiguredLimitAcceptsLimitAndRejectsSuccessorFromHeader(int limit)
    {
        static byte[] Header(int length) => length <= 125 ? new byte[] { 0x82, (byte)length }
            : length <= 65535 ? new byte[] { 0x82, 126, (byte)(length >> 8), (byte)length }
            : new byte[] { 0x82, 127, 0, 0, 0, 0, (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
        var payload = Enumerable.Range(0, limit + 1).Select(i => (byte)(i * 7)).ToArray();

        using (var input = new Input(Join(Header(limit), payload[..limit])))
        using (var connection = Connection(input, limit))
        {
            Dataframe? frame = null; Exception? error = null;
            try { frame = await connection.ReadDataframeAsync(CancellationToken.None); }
            catch (Exception e) { error = e; }
            Assert.True(error is null && frame is not null, $"Frame of exactly the limit {limit} was refused: {error}");
            Message(frame!, 2, payload[..limit]);
            int wire = Header(limit).Length + limit;
            Assert.True(input.BytesRead == wire && input.Attempts == wire, "Frame at the limit read differs from its wire size");
        }

        using (var input = new Input(Join(Header(limit + 1), payload)))
        using (var connection = Connection(input, limit))
        {
            await Assert.ThrowsAsync<WebsocketClientLiteException>(() => connection.ReadDataframeAsync(CancellationToken.None));
            int header = Header(limit + 1).Length;
            Assert.True(input.BytesRead == header && input.Attempts == header,
                $"Frame one above the limit {limit} read {input.BytesRead} bytes in {input.Attempts} attempts, expected {header}");
        }
    }
}
