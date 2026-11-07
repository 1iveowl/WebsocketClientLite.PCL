using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.Service;
using Xunit;
using Xunit.Abstractions;

namespace WebsocketClientLiteTest;

public class QueuedWriteTests(ITestOutputHelper output)
{
    private sealed class Harness(ITestOutputHelper output) : IAsyncDisposable
    {
        internal readonly ConcurrentQueue<string> Events = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ConcurrentQueue<byte> Opcodes = new();
        internal readonly ConcurrentQueue<Task> Tasks = new();
        internal readonly MemoryStream Stream = new();
        internal readonly TcpConnectionService Tcp = new(() => false, (_, _, _, _) => true,
            (_, _) => Task.CompletedTask, (_, _) => { }, true, new TcpClient());
        internal WebsocketSenderHandler Sender = null!;
        private int active;
        internal int MaxActive;
        internal void Initialize()
        {
            typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Tcp, Stream);
            Sender = new(Tcp, (_, _) => { }, async (_, bytes, _, _) =>
            {
                int owners = Interlocked.Increment(ref active);
                MaxActive = Math.Max(MaxActive, owners);
                try
                {
                    Assert.True(owners == 1, "Overlapping frame delegates");
                    Opcodes.Enqueue((byte)(bytes[0] & 15));
                    Events.Enqueue("delegate entered; frame held"); Entered.TrySetResult();
                    await Resume.Task;
                    Events.Enqueue("delegate settled");
                }
                finally { Interlocked.Decrement(ref active); }
            }, false);
        }
        internal Task Track(Task task) { Tasks.Enqueue(task); return task; }
        internal async Task<T> Wait<T>(Task<T> task)
        {
            await Wait((Task)task); return await task;
        }
        internal async Task Wait(Task task)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException e) { throw new TimeoutException("Stuck owner: " + string.Join(" -> ", Events), e); }
        }
        public async ValueTask DisposeAsync()
        {
            Resume.TrySetResult();
            try { await Wait(Task.WhenAll(Tasks)); }
            catch (Exception e) { Events.Enqueue("observed cleanup: " + e.Message); }
            Tcp.Dispose(); output.WriteLine(string.Join(" -> ", Events));
        }
    }

    [Fact]
    public async Task AbortDrainsNonCancelableFrameWaiterAfterOwnerSettles()
    {
        await using var h = new Harness(output); h.Initialize();
        var owner = h.Track(h.Tcp.WriteSerializedAsync(async (_, _) =>
        {
            h.Events.Enqueue("transport owner entered"); h.Entered.TrySetResult(); await h.Resume.Task;
        }, CancellationToken.None));
        await h.Wait(h.Entered.Task);
        int queuedCalls = 0;
        var queued = h.Track(h.Tcp.WriteSerializedAsync((_, _) =>
        {
            Interlocked.Increment(ref queuedCalls); return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.False(queued.IsCompleted); h.Events.Enqueue("noncancelable frame waiter queued");
        h.Tcp.Dispose(); h.Events.Enqueue("transport disposed");
        h.Resume.TrySetResult(); await h.Wait(owner); h.Events.Enqueue("owner settled");
        await h.Wait(Assert.ThrowsAsync<ObjectDisposedException>(() => queued));
        Assert.Equal(0, queuedCalls);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationBeforeOrWhileQueuedPreservesAdmission(bool frameGate, bool preCancelled)
    {
        await using var h = new Harness(output); h.Initialize();
        using var cancellation = new CancellationTokenSource();
        // Ping holds only frame ownership; data holds both message and frame ownership.
        var owner = h.Track(frameGate ? h.Sender.SendPing("owner") : h.Sender.SendText("owner"));
        await h.Wait(h.Entered.Task);
        if (preCancelled) cancellation.Cancel();
        var queued = h.Track(h.Sender.SendText("cancelled", cancellation.Token));
        h.Events.Enqueue(frameGate ? "data waiting-frame (message held)" : "data waiting-message");
        if (!preCancelled) { Assert.False(queued.IsCompleted); cancellation.Cancel(); }
        // Frame-wait failures are wrapped by the existing SendError contract;
        // message-wait cancellation is propagated directly. Neither reached the delegate.
        if (frameGate && !preCancelled)
        {
            var error = await h.Wait(Assert.ThrowsAsync<WebsocketClientLite.CustomException.WebsocketClientLiteException>(() => queued));
            Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        }
        else await h.Wait(Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued));
        Assert.Single(h.Opcodes);
        h.Resume.TrySetResult(); await h.Wait(owner);
        // Stay within two data sends and one control contender per connection.
        await h.Wait(h.Track(frameGate ? h.Sender.SendText("admitted") : h.Sender.SendPing("admitted")));
        Assert.Equal(new byte[] { frameGate ? (byte)9 : (byte)1, frameGate ? (byte)1 : (byte)9 }, h.Opcodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancelledControlNeverAcquiresFrameOwnership(bool held)
    {
        await using var h = new Harness(output); h.Initialize();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Task owner = Task.CompletedTask;
        if (held)
        {
            owner = h.Track(h.Sender.SendText("owner")); await h.Wait(h.Entered.Task);
        }
        var cancelled = h.Track(h.Sender.SendPing("cancelled", cancellation.Token));
        var error = await h.Wait(Assert.ThrowsAsync<WebsocketClientLite.CustomException.WebsocketClientLiteException>(() => cancelled));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Equal(held ? 1 : 0, h.Opcodes.Count);
        h.Resume.TrySetResult(); await h.Wait(owner);
        await h.Wait(h.Track(h.Sender.SendText("admitted")));
        Assert.Equal(held ? 2 : 1, h.Opcodes.Count);
    }

    [Fact]
    public async Task FrameSerializationAdmitsTwoDataSendsAndOneControlWithoutOverlap()
    {
        await using var h = new Harness(output); h.Initialize();
        var owner = h.Track(h.Sender.SendText("first")); await h.Wait(h.Entered.Task);
        var data = h.Track(h.Sender.SendText("second")); h.Events.Enqueue("second waiting-message");
        var control = h.Track(h.Sender.SendPing("control")); h.Events.Enqueue("control waiting-frame");
        Assert.True(h.MaxActive == 1, "Overlapping frame delegates");
        Assert.False(data.IsCompleted); Assert.False(control.IsCompleted); Assert.Single(h.Opcodes);
        h.Resume.TrySetResult(); await h.Wait(Task.WhenAll(owner, data, control));
        Assert.True(h.MaxActive == 1, "Overlapping frame delegates");
        // Either post-release order is legal: no FIFO assumption.
        Assert.Equal(2, h.Opcodes.Count(op => op == 1)); Assert.Equal(1, h.Opcodes.Count(op => op == 9));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TerminalNotificationRejectsDataWaitingAtEitherGate(bool frameGate, bool readFailure)
    {
        await using var h = new Harness(output); h.Initialize();
        var owner = h.Track(frameGate ? h.Sender.SendPing("owner") : h.Sender.SendText("owner"));
        await h.Wait(h.Entered.Task);
        var queued = h.Track(h.Sender.SendText("queued")); Assert.False(queued.IsCompleted);
        h.Events.Enqueue(frameGate ? "data waiting-frame" : "data waiting-message");
        if (readFailure) h.Sender.NotifyReadFailure(); else h.Sender.NotifyPeerClose();
        h.Events.Enqueue(readFailure ? "read failure" : "peer Close");
        h.Resume.TrySetResult(); await h.Wait(owner);
        await h.Wait(Assert.ThrowsAsync<WebsocketClientLite.CustomException.WebsocketClientLiteException>(() => queued));
        Assert.Single(h.Opcodes);
    }

    [Fact]
    public async Task AbortAllowsQueuedCancellationWhileOwnerRemainsInFlight()
    {
        await using var h = new Harness(output); h.Initialize();
        using var cancellation = new CancellationTokenSource();
        var owner = h.Track(h.Sender.SendText("owner")); await h.Wait(h.Entered.Task);
        var queued = h.Track(h.Sender.SendPing("queued", cancellation.Token));
        Assert.False(queued.IsCompleted); h.Events.Enqueue("control waiting-frame");
        h.Tcp.Dispose(); cancellation.Cancel(); h.Events.Enqueue("abort and cancel waiter");
        var error = await h.Wait(Assert.ThrowsAsync<WebsocketClientLite.CustomException.WebsocketClientLiteException>(() => queued));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.False(owner.IsCompleted); Assert.Single(h.Opcodes);
        h.Resume.TrySetResult(); await h.Wait(owner);
    }

}
