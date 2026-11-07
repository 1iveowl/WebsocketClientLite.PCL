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
        internal void Initialize()
        {
            typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Tcp, Stream);
            Sender = new(Tcp, (_, _) => { }, async (_, bytes, _, _) =>
            {
                int owners = Interlocked.Increment(ref active);
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
}
