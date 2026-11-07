using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Service;
using WebsocketClientLite.VerifiedProtocol;
using Xunit;

namespace WebsocketClientLiteTest;

public class ProtocolStateTests
{
    private static WebsocketSenderHandler Sender(Func<Stream, byte[], int, CancellationToken, Task> write)
    {
        var connection = new TcpConnectionService(() => false, (o, c, ch, e) => true,
            (c, u) => Task.CompletedTask, (s, e) => { }, true, new TcpClient());
        typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(connection, new MemoryStream());
        return new WebsocketSenderHandler(connection, (s, e) => { }, write, false);
    }

    [Fact]
    public async Task ListOwnsDataMessageWhileControlsCanInterleave()
    {
        var frames = new List<byte>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = Sender(async (s, b, n, ct) =>
        {
            frames.Add(b[0]);
            if (frames.Count == 1) { entered.SetResult(); await release.Task; }
        });
        var list = sender.SendText(new[] { "first", "last" });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other = sender.SendText("other");
        var ping = sender.SendPing("ping");
        release.SetResult();
        await Task.WhenAll(list, other, ping).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 0x01, 0x80, 0x81 }, frames.Where(b => (b & 15) < 8));
        Assert.Contains((byte)0x89, frames);
    }

    [Fact]
    public async Task InvalidLaterStringWritesNothingAndSenderRemainsUsable()
    {
        var frames = new List<byte>();
        var sender = Sender((s, b, n, ct) => { frames.Add(b[0]); return Task.CompletedTask; });
        await Assert.ThrowsAsync<EncoderFallbackException>(() => sender.SendText(new[] { "first", "\uD800" }));
        Assert.Empty(frames);
        await sender.SendText("valid");
        Assert.Equal(new byte[] { 0x81 }, frames);
    }

    [Fact]
    public async Task CancellationAfterFirstFragmentPreventsReuseButAllowsClose()
    {
        using var cancellation = new CancellationTokenSource();
        var frames = new List<byte>();
        var sender = Sender((s, b, n, ct) =>
        {
            frames.Add(b[0]);
            if (frames.Count == 1) cancellation.Cancel();
            return Task.CompletedTask;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendText(new[] { "first", "last" }, cancellation.Token));
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => sender.SendText("next"));
        await sender.SendCloseHandshakeAsync(WebsocketClientLite.Model.StatusCodes.GoingAway);
        Assert.Equal(new byte[] { 0x01, 0x88 }, frames);
    }

    [Fact]
    public async Task PartialWriteFailureForbidsFurtherFrames()
    {
        int writes = 0;
        var sender = Sender((s, b, n, ct) => { writes++; throw new IOException("partial write"); });
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => sender.SendText("first"));
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => sender.SendText("next"));
        await sender.SendCloseHandshakeAsync(WebsocketClientLite.Model.StatusCodes.GoingAway);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ManualSequenceRejectsOrphansAndNewDataWithoutChangingPendingMessage()
    {
        var frames = new List<byte>();
        var sender = Sender((s, b, n, ct) => { frames.Add(b[0]); return Task.CompletedTask; });
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => sender.SendText("orphan", OpcodeKind.Continuation, FragmentKind.Last));
        await sender.SendText("first", OpcodeKind.Text, FragmentKind.First);
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => sender.SendText("new"));
        await sender.SendText("last", OpcodeKind.Continuation, FragmentKind.Last);
        Assert.Equal(new byte[] { 0x01, 0x80 }, frames);
    }

    [Fact]
    public async Task ReceivedCloseBlocksDataAndSendsOnlyOneResponse()
    {
        var frames = new List<byte>();
        var sender = Sender((s, b, n, ct) => { frames.Add(b[0]); return Task.CompletedTask; });
        sender.NotifyPeerClose();
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => sender.SendText("late"));
        await sender.SendCloseHandshakeAsync(WebsocketClientLite.Model.StatusCodes.GoingAway);
        await sender.SendCloseHandshakeAsync(WebsocketClientLite.Model.StatusCodes.GoingAway);
        await sender.WaitForPeerCloseAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 0x88 }, frames);
    }

    [Fact]
    public async Task PongIsAllowedUntilPeerCloseThenSuppressed()
    {
        var frames = new List<byte>();
        var sender = Sender((s, b, n, ct) => { frames.Add(b[0]); return Task.CompletedTask; });
        await sender.SendCloseHandshakeAsync(WebsocketClientLite.Model.StatusCodes.GoingAway);
        var ping = new WebsocketClientLite.Model.Dataframe { Payload = new byte[] { 42 } };
        await sender.SendPong(ping, CancellationToken.None);
        sender.NotifyPeerClose();
        await sender.SendPong(ping, CancellationToken.None);
        Assert.Equal(new byte[] { 0x88, 0x8a }, frames);
    }

    [Fact]
    public async Task BinaryListUsesOneCompleteFragmentSequence()
    {
        var frames = new List<byte>();
        var sender = Sender((s, b, n, ct) => { frames.Add(b[0]); return Task.CompletedTask; });
        await sender.SendBinary(new[] { new byte[] { 1 }, Array.Empty<byte>(), new byte[] { 2 } }, CancellationToken.None);
        Assert.Equal(new byte[] { 0x02, 0x00, 0x80 }, frames);
    }

    [Fact]
    public void MergePreservesEmptyAndNonemptySegmentsInOrder()
    {
        byte[][] segments = { Array.Empty<byte>(), new byte[] { 1, 2 }, Array.Empty<byte>(), new byte[] { 3 }, Array.Empty<byte>() };
        var merged = Protocol.Merge(segments, 3);
        Assert.Equal(new byte[] { 1, 2, 3 }, merged);
        Assert.Equal(new byte[] { 1, 2 }, segments[1]);
        merged = Protocol.Merge(Array.Empty<byte[]>(), 0);
        Assert.Empty(merged);
    }

    [Fact]
    public void BulkCopyContractPreservesSourceAndDestinationOutsideSlice()
    {
        foreach (int size in new[] { 0, 1, 4, 125, 1024 })
        {
            var source = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            var original = source.ToArray();
            var destination = Enumerable.Repeat((byte)0xcc, size + 7).ToArray();
            NativeCopy.CopySlice(source, destination, 3);
            Assert.Equal(original, source);
            Assert.Equal(source, destination.Skip(3).Take(size));
            Assert.All(destination.Take(3).Concat(destination.Skip(3 + size)), b => Assert.Equal(0xcc, b));
        }
    }

    [Fact]
    public void BulkMergeMatchesScalarProofAndIndependentConcatenation()
    {
        var random = new Random(6455);
        for (int trial = 0; trial < 100; trial++)
        {
            var segments = Enumerable.Range(0, random.Next(0, 20)).Select(_ =>
            {
                var bytes = new byte[random.Next(0, 200)];
                random.NextBytes(bytes);
                return bytes;
            }).ToArray();
            var expected = segments.SelectMany(segment => segment).ToArray();
            var merged = Protocol.Merge(segments, (uint)expected.Length);
            Assert.Equal(expected, merged);
            Assert.Equal(Protocol.MergeScalar(segments, (uint)expected.Length), merged);
        }
    }

    [Fact]
    public async Task CloseResponseTimeoutMakesTheTransportTerminal()
    {
        var frames = new List<byte>();
        var sender = Sender((s, b, n, ct) => { frames.Add(b[0]); return Task.CompletedTask; });
        await sender.SendCloseHandshakeAsync(WebsocketClientLite.Model.StatusCodes.GoingAway);
        await sender.WaitForPeerCloseAsync(TimeSpan.FromMilliseconds(20)).WaitAsync(TimeSpan.FromSeconds(5));
        await sender.SendPong(new WebsocketClientLite.Model.Dataframe { Payload = new byte[] { 1 } }, CancellationToken.None);
        await Assert.ThrowsAsync<WebsocketClientLiteException>(() => sender.SendText("late"));
        Assert.Equal(new byte[] { 0x88 }, frames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(int.MaxValue - 14)]
    public void EncoderDecoderComposition(int length)
    {
        Protocol.HeaderRoundTrip((uint)length, out ulong decoded, out uint header);
        Assert.Equal((ulong)length, decoded);
        Assert.Equal(length <= 125 ? 2U : length <= 65535 ? 4U : 10U, header);
    }

    [Fact]
    public void ClosingRequiresBothDirectionsOrTerminalEvent()
    {
        Protocol.CloseStep(0, 0, out byte sent, out bool send, out bool release);
        Assert.True(send); Assert.False(release);
        Protocol.CloseStep(sent, 0, out byte repeated, out send, out release);
        Assert.Equal(sent, repeated); Assert.False(send); Assert.False(release);
        Protocol.CloseStep(sent, 1, out _, out send, out release);
        Assert.False(send); Assert.True(release);
        Protocol.CloseStep(0, 1, out byte received, out _, out release);
        Assert.False(release);
        Protocol.CloseStep(received, 0, out _, out send, out release);
        Assert.True(send); Assert.True(release);
        Protocol.CloseStep(sent, 2, out byte timedOut, out _, out release);
        Assert.True(release);
        Protocol.CloseStep(timedOut, 0, out _, out send, out _);
        Assert.False(send);
    }
}
