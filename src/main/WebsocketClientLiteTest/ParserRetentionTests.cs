using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;
using Xunit;

namespace WebsocketClientLiteTest;

public class ParserRetentionTests
{
    // A single reader supplies one byte per read, exercising both TFM read overloads.
    // BytesRead and Attempts distinguish admission rejection from an absent body/EOF.
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
    // Independent literal RFC wire encoding; never calls production composition/Step/Merge.
    private static byte[] Wire(byte opcode, bool fin, params byte[] payload) =>
        new[] { (byte)(opcode | (fin ? 128 : 0)), checked((byte)payload.Length) }.Concat(payload).ToArray();
    private static byte[] Join(params byte[][] pieces) => pieces.SelectMany(p => p).ToArray();
    private static void Message(Dataframe frame, byte opcode, byte[] expected)
    {
        Assert.True(frame.Payload!.SequenceEqual(expected), "Retained bytes differ from independent concatenation");
        Assert.True(frame.Length == (ulong)expected.Length, "Retained Length differs from byte total");
        Assert.True(frame.FIN && frame.Fragment == FragmentKind.None && (byte)frame.Opcode == opcode,
            "Original message metadata was not preserved");
    }
    private static async Task<(List<Dataframe> Frames, Exception? Error)> Observe(TcpConnectionService connection)
    {
        using var parser = new WebsocketParserHandler(connection);
        var frames = new List<Dataframe>();
        var done = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = parser.DataframeObservable().Subscribe(f => frames.Add(f!), e => done.TrySetResult(e), () => done.TrySetResult(null));
        return (frames, await done.Task.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task SmallSequencesPreserveBytesMetadataAndReset()
    {
        // Exhaustive bounded lengths 0..2 in three fragments: zero, one and multiple
        // retained arrays, empty continuations, both data opcodes and controls.
        foreach (byte opcode in new byte[] { 1, 2 })
        for (int a = 0; a <= 2; a++)
        for (int b = 0; b <= 2; b++)
        for (int c = 0; c <= 2; c++)
        {
            byte[] x = Enumerable.Repeat((byte)'a', a).ToArray(), y = Enumerable.Repeat((byte)'b', b).ToArray(), z = Enumerable.Repeat((byte)'c', c).ToArray();
            using var input = new Input(Join(Wire(opcode, false, x), Wire(9, true, 90), Wire(0, false, y),
                Wire(10, true, 91), Wire(0, true, z), Wire(2, true, 42), Wire(1, true), Wire(8, true)));
            using var connection = Connection(input, 6);
            var (frames, error) = await Observe(connection);
            Assert.True(error is null, "Sequence rejected: " + error);
            Assert.Equal(6, frames.Count);
            Message(frames[0], 9, new byte[] { 90 }); Message(frames[1], 10, new byte[] { 91 });
            Message(frames[2], opcode, Join(x, y, z));
            Message(frames[3], 2, new byte[] { 42 }); Message(frames[4], 1, Array.Empty<byte>());
            Assert.Equal(OpcodeKind.Close, frames[5].Opcode);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task AccumulatedLimitRejectsBeforePayloadRead(int limit)
    {
        // Each prefix length probes the externally observable remaining budget.
        for (int prefix = 0; prefix <= limit; prefix++)
        foreach (int total in new[] { limit - 1, limit, limit + 1 }.Where(n => n >= prefix))
        {
            var first = Enumerable.Repeat((byte)7, prefix).ToArray();
            var last = Enumerable.Repeat((byte)8, total - prefix).ToArray();
            var beforeLast = Join(Wire(2, false, first), Wire(9, true, 99), Wire(0, false), Wire(10, true, 98));
            using var input = new Input(Join(beforeLast, Wire(0, true, last), Wire(8, true)));
            using var connection = Connection(input, limit);
            var (frames, error) = await Observe(connection);
            if (total > limit)
            {
                Assert.IsType<WebsocketClientLiteException>(error);
                Assert.True(input.BytesRead == beforeLast.Length + 2 && input.Attempts == input.BytesRead,
                    "Accumulated limit consumed rejected payload");
                Assert.Equal(2, frames.Count);
            }
            else
            {
                Assert.True(error is null, "Control payload changed retained budget: " + error);
                Message(frames[2], 2, Join(first, last));
            }
        }
    }

    [Fact]
    public async Task ControlPayloadDoesNotChangeRetainedTotal()
    {
        using var input = new Input(Join(Wire(2, false, 1, 2), Wire(9, true, 99), Wire(0, true, 3, 4), Wire(8, true)));
        using var connection = Connection(input, 4);
        var (frames, error) = await Observe(connection);
        Assert.True(error is null && frames.Count == 3, "Control payload changed retained budget: " + error);
        Message(frames[1], 2, new byte[] { 1, 2, 3, 4 });
        Assert.True(input.BytesRead == 13, "Control sequence read budget differs from wire size");
    }

    [Fact]
    public async Task ExtendedHeaderCapturesScratchBeforeReuse()
    {
        var expected = Enumerable.Range(0, 126).Select(i => (byte)i).ToArray();
        using var input = new Input(Join(new byte[] { 0x82, 126, 0, 126 }, expected));
        using var connection = Connection(input);
        Dataframe? frame = null; Exception? error = null;
        try { frame = await connection.ReadDataframeAsync(CancellationToken.None); }
        catch (Exception e) { error = e; }
        Assert.True(error is null && frame is not null, "Header scratch capture corrupted admission: " + error);
        Message(frame!, 2, expected);
        Assert.True(input.BytesRead == 130 && input.Attempts == 130, "Header read budget differs from wire size");
    }

    [Theory]
    [InlineData(2147483647UL, 1U)]
    [InlineData(2147483648UL, 0U)]
    [InlineData(9223372036854775808UL, 0U)]
    public async Task HugeLengthsRejectOnHeaderOnly(ulong length, uint retained)
    {
        var bytes = new List<byte> { 0x80, 127 };
        for (int shift = 56; shift >= 0; shift -= 8) bytes.Add((byte)(length >> shift));
        using var input = new Input(bytes.ToArray()); using var connection = Connection(input);
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => connection.ReadDataframeAsync(CancellationToken.None, 2, retained));
        Assert.True(input.BytesRead == 10 && input.Attempts == 10, "Large admission attempted payload allocation/read");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteMessageTerminatesWithoutEmission(bool cancel)
    {
        // EOF/cancellation at next header, partial extended header and partial payload.
        foreach (var tail in new[] { Array.Empty<byte>(), new byte[] { 0x80, 126, 0 }, new byte[] { 0x80, 2, 9 } })
        {
            using var input = new Input(Join(Wire(2, false, 7), tail), cancel);
            using var connection = Connection(input);
            var (frames, error) = await Observe(connection);
            Assert.Empty(frames);
            if (cancel) Assert.Null(error); else Assert.IsType<WebsocketClientLiteException>(error);
        }
    }

    [Fact]
    public async Task CloseAndInvalidContinuationDoNotLeakRetainedData()
    {
        using var input = new Input(Join(Wire(2, false, 7), Wire(8, true), Wire(0, true, 8)));
        using var connection = Connection(input);
        var (frames, error) = await Observe(connection);
        Assert.Null(error); Assert.Equal(OpcodeKind.Close, Assert.Single(frames).Opcode);
        Assert.Equal(5, input.BytesRead);
        foreach (var prefix in new[] { Array.Empty<byte>(), Wire(2, true, 7) })
        {
            using var invalid = new Input(Join(prefix, Wire(0, true, 8)));
            using var badConnection = Connection(invalid);
            var result = await Observe(badConnection);
            Assert.IsType<WebsocketClientLiteException>(result.Error);
            Assert.True(invalid.BytesRead == prefix.Length + 2 && invalid.Attempts == invalid.BytesRead, "Orphan continuation consumed payload");
            Assert.Equal(prefix.Length == 0 ? 0 : 1, result.Frames.Count);
        }
    }

    [Fact]
    public async Task SplitUtf8IsValidatedOnlyWhenComplete()
    {
        foreach (byte final in new byte[] { 0xac, 0xff })
        {
            using var input = new Input(Join(Wire(1, false, 0xe2), Wire(9, true, 1), Wire(0, false, 0x82), Wire(0, true, final), Wire(8, true)));
            using var connection = Connection(input, 3);
            var (frames, error) = await Observe(connection);
            if (final == 0xac) { Assert.Null(error); Message(frames[1], 1, new byte[] { 0xe2, 0x82, 0xac }); Assert.Equal("€", frames[1].Message); }
            else { Assert.IsType<WebsocketClientLiteException>(error); Assert.Equal(OpcodeKind.Ping, Assert.Single(frames).Opcode); }
        }
    }

    [Fact]
    public async Task HandshakeConsumesScratchImmediatelyAndStopsBeforeFrame()
    {
        using var input = new Input(Array.Empty<byte>()); // replaced by writer after it sees the random request key
        using var connection = Connection(input);
        Input? response = null; int headerLength = 0;
        var sender = new WebsocketSenderHandler(connection, (_, _) => { }, (_, buffer, count, _) =>
        {
            string request = Encoding.ASCII.GetString(buffer, 0, count);
            string key = request.Split("\r\n").Single(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.Ordinal)).Split(':')[1].Trim();
            string accept = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
            headerLength = header.Length;
            response = new Input(Join(header, Wire(2, true, 17), Wire(8, true)));
            typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connection, response);
            return Task.CompletedTask;
        }, false);
        var handshake = new HandshakeHandler(connection, (_, _) => { });
        var states = await handshake.Handshake(new Uri("ws://localhost/"), sender, TimeSpan.FromSeconds(15), CancellationToken.None).ToArray();
        Assert.Equal(HandshakeStateKind.HandshakeCompletedSuccessfully, Assert.Single(states).handshakeState);
        Assert.True(response!.BytesRead == headerLength && response.Attempts == headerLength, "Handshake consumed first frame or reused stale scratch");
        var result = await Observe(connection);
        Assert.Null(result.Error); Message(result.Frames[0], 2, new byte[] { 17 });
    }
}
