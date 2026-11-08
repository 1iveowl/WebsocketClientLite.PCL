using System;
using System.Buffers;
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
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;
using Xunit;
using Xunit.Abstractions;

namespace WebsocketClientLiteTest;

public class PayloadLeaseTests(ITestOutputHelper output)
{
    private sealed class Lease(byte[] bytes, int count)
    {
        internal readonly byte[] Bytes = bytes;
        internal readonly int Count = count;
        internal int Returns;
        internal bool Clear, Early;
        internal Task? Write;
        internal readonly TaskCompletionSource Returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Pool : ArrayPool<byte>
    {
        internal readonly List<Lease> Leases = new();
        public override byte[] Rent(int minimumLength)
        {
            var bytes = Enumerable.Repeat((byte)0xcd, minimumLength + 19).ToArray();
            Leases.Add(new(bytes, minimumLength)); return bytes;
        }
        public override void Return(byte[] array, bool clearArray = false)
        {
            var lease = Leases.Single(l => ReferenceEquals(l.Bytes, array));
            lease.Returns++; lease.Clear = clearArray;
            lease.Early |= lease.Write is { IsCompleted: false };
            // Poison only while the pool owns the array. Harness readers check ownership first.
            Array.Fill(array, (byte)0xdd);
            lease.Returned.TrySetResult();
        }
        internal void Complete(int count)
        {
            Assert.Equal(count, Leases.Count);
            foreach (var lease in Leases)
            {
                Assert.True(lease.Returns == 1, "Lease not returned exactly once");
                Assert.True(lease.Clear, "Lease clear-on-return not requested");
                Assert.False(lease.Early, "Lease returned before write completion");
            }
        }
    }
    private sealed class Harness(ITestOutputHelper output) : IAsyncDisposable
    {
        internal readonly Pool Pool = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<Task> Tasks = new();
        internal readonly List<byte[]> Frames = new();
        internal readonly TcpConnectionService Tcp = new(() => false, (_, _, _, _) => true,
            (_, _) => Task.CompletedTask, (_, _) => { }, true, new TcpClient());
        internal WebsocketSenderHandler Sender = null!;
        internal int Writes;
        internal void Initialize()
        {
            typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Tcp, new MemoryStream());
            Sender = new(Tcp, (_, _) => { }, (_, bytes, count, _) =>
            {
                Writes++; Entered.TrySetResult();
                var lease = Pool.Leases.Single(l => ReferenceEquals(l.Bytes, bytes));
                Assert.True(lease.Returns == 0, "Lease returned before write completion");
                Assert.True(count == lease.Count, "Write count includes uninitialized capacity");
                Assert.All(bytes.Skip(count), b => Assert.Equal(0xcd, b));
                Frames.Add(bytes.Take(count).ToArray());
                lease.Write = Writes == 1 ? Completion.Task : Task.CompletedTask;
                return lease.Write;
            }, false, framePool: Pool);
        }
        internal Task Track(Task task) { Tasks.Add(task); return task; }
        internal static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(15));
        internal void Held()
        {
            Assert.True(Pool.Leases[0].Returns == 0, "Lease returned before write completion");
            Assert.False(Completion.Task.IsCompleted);
        }
        internal void Finish(Exception? error = null)
        {
            var lease = Pool.Leases[0];
            // Last read occurs before the delegate's Task settles, never after Return.
            Assert.True(lease.Returns == 0, "Lease returned before write completion");
            Assert.Equal(Frames[0], lease.Bytes.Take(lease.Count).ToArray());
            if (error is null) Completion.TrySetResult(); else Completion.TrySetException(error);
        }
        public async ValueTask DisposeAsync()
        {
            Completion.TrySetResult();
            // Observe expected failures, but a cleanup timeout must fail the test.
            try { await Wait(Task.WhenAll(Tasks)); }
            catch (Exception e) when (e is not TimeoutException) { output.WriteLine("Observed cleanup: " + e.Message); }
            finally { Tcp.Dispose(); output.WriteLine($"Rents={Pool.Leases.Count}, writes={Writes}, returns={string.Join(',', Pool.Leases.Select(l => l.Returns))}"); }
        }
    }
    private static void Wire(byte[] frame, byte first, byte[] payload)
    {
        int header = payload.Length < 126 ? 2 : payload.Length <= 65535 ? 4 : 10;
        var expected = new List<byte> { first, (byte)(128 | (header == 2 ? payload.Length : header == 4 ? 126 : 127)) };
        if (header > 2) for (int shift = (header - 3) * 8; shift >= 0; shift -= 8) expected.Add((byte)((ulong)payload.Length >> shift));
        var key = frame.Skip(header).Take(4).ToArray();
        expected.AddRange(key);
        expected.AddRange(payload.Select((b, i) => (byte)(b ^ key[i % 4])));
        Assert.Equal(expected, frame);
    }

    [Fact]
    public async Task PausedWriteRetainsLeaseUntilCompletion()
    {
        await using var h = new Harness(output); h.Initialize();
        byte[] input = { 1, 2, 3, 4, 5 };
        var send = h.Track(h.Sender.SendBinary(input, default));
        await Harness.Wait(h.Entered.Task);
        // Propagate delegate assertions before inspecting the pending lease.
        if (send.IsCompleted) await send;
        h.Held();
        Wire(h.Frames[0], 0x82, input); h.Finish(); await Harness.Wait(send);
        h.Pool.Complete(1); Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, input);
    }

    [Theory]
    [InlineData(0)] [InlineData(125)] [InlineData(126)] [InlineData(65535)] [InlineData(65536)]
    public async Task InitializedCountMatchesIndependentWireBytes(int length)
    {
        await using var h = new Harness(output); h.Initialize(); h.Completion.SetResult();
        var input = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var saved = input.ToArray();
        await Harness.Wait(h.Track(h.Sender.SendBinary(input, default)));
        Wire(h.Frames[0], 0x82, saved); Assert.Equal(saved, input); h.Pool.Complete(1);
    }

    [Fact]
    public async Task StableAliasedListInputsRemainUnchanged()
    {
        await using var h = new Harness(output); h.Initialize();
        byte[] shared = { 7, 8, 9 }; byte[][] inputs = { shared, shared };
        var send = h.Track(h.Sender.SendBinary(inputs, default));
        await Harness.Wait(h.Entered.Task); h.Held(); h.Finish(); await Harness.Wait(send);
        Wire(h.Frames[0], 0x02, new byte[] { 7, 8, 9 }); Wire(h.Frames[1], 0x80, new byte[] { 7, 8, 9 });
        Assert.Same(inputs[0], inputs[1]); Assert.Equal(new byte[] { 7, 8, 9 }, shared); h.Pool.Complete(2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task GateCancellationReturnsOnlyAcquiredLeases(bool frameGate)
    {
        await using var h = new Harness(output); h.Initialize(); using var ct = new CancellationTokenSource();
        var owner = h.Track(frameGate ? h.Sender.SendPing("owner") : h.Sender.SendText("owner"));
        await Harness.Wait(h.Entered.Task);
        var queued = h.Track(h.Sender.SendText("queued", ct.Token)); Assert.False(queued.IsCompleted);
        Assert.Equal(frameGate ? 2 : 1, h.Pool.Leases.Count); ct.Cancel();
        await Harness.Wait(Assert.ThrowsAnyAsync<Exception>(() => queued));
        if (frameGate) Assert.Equal(1, h.Pool.Leases[1].Returns);
        h.Held(); h.Finish(); await Harness.Wait(owner); Assert.Equal(1, h.Writes); h.Pool.Complete(frameGate ? 2 : 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedOrPartialWriteReturnsAfterSettlement(bool partial)
    {
        await using var h = new Harness(output); h.Initialize();
        var send = h.Track(h.Sender.SendText("héllo")); await Harness.Wait(h.Entered.Task); h.Held();
        Wire(h.Frames[0], 0x81, Encoding.UTF8.GetBytes("héllo"));
        if (partial) await h.Tcp.ConnectionStream.WriteAsync(h.Frames[0].AsMemory(0, 3));
        h.Finish(new IOException("controlled write failure"));
        var error = await Assert.ThrowsAsync<WebsocketClientLiteException>(() => Harness.Wait(send));
        Assert.IsType<IOException>(error.InnerException); h.Pool.Complete(1);
    }

    [Fact]
    public async Task InvalidTextIsRejectedBeforeRent()
    {
        await using var h = new Harness(output); h.Initialize();
        await Assert.ThrowsAsync<EncoderFallbackException>(() => h.Sender.SendText("\ud800"));
        await Assert.ThrowsAsync<EncoderFallbackException>(() => h.Sender.SendText(new[] { "valid", "\ud800" }));
        h.Pool.Complete(0); Assert.Equal(0, h.Writes);
    }

    [Fact]
    public async Task SuppressedQueuedPongReturnsWithoutDelegateUse()
    {
        await using var h = new Harness(output); h.Initialize();
        var owner = h.Track(h.Sender.SendText("owner")); await Harness.Wait(h.Entered.Task);
        var pong = h.Track(h.Sender.SendPong(new Dataframe { Payload = new byte[] { 42 } }, default));
        Assert.False(pong.IsCompleted); Assert.Equal(2, h.Pool.Leases.Count); Assert.All(h.Pool.Leases, l => Assert.Equal(0, l.Returns));
        h.Sender.NotifyPeerClose(); h.Held(); h.Finish(); await Harness.Wait(Task.WhenAll(owner, pong));
        Assert.Equal(1, h.Writes); Assert.Null(h.Pool.Leases[1].Write); h.Pool.Complete(2);
    }

    [Fact]
    public async Task SendObservationTimeoutRetainsPendingLease()
    {
        await using var h = new Harness(output); h.Initialize();
        using var connection = new WebsocketConnectionHandler(h.Tcp, new WebsocketParserHandler(h.Tcp), (_, _) => { }, (_, _) => h.Sender);
        var disconnect = h.Track(connection.DisconnectWebsocket(h.Sender));
        await Harness.Wait(h.Entered.Task); h.Held();
        await Harness.Wait(disconnect); // Real production five-second Rx observation timeout.
        h.Held(); Assert.Equal(1, h.Writes);
        // Observe the original close send through the return checkpoint; disconnect no longer awaits it.
        h.Finish();
        await Harness.Wait(h.Pool.Leases[0].Returned.Task);
        h.Pool.Complete(1);
    }
}
