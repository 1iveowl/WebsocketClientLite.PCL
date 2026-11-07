using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;
using Xunit;
using Xunit.Abstractions;

namespace WebsocketClientLiteTest;

// Schedules are specified in verification/controlled-close.md. No sleep establishes ordering.
public class ControlledCloseTests(ITestOutputHelper output)
{
    private sealed class Harness(ITestOutputHelper output) : IAsyncDisposable
    {
        internal readonly ConcurrentQueue<string> Events = new();
        internal readonly ConcurrentQueue<byte[]> Frames = new();
        internal readonly TaskCompletionSource Entered = Signal();
        internal readonly TaskCompletionSource Resume = Signal();
        internal readonly TaskCompletionSource Waiting = Signal();
        internal readonly TaskCompletionSource Expire = Signal();
        internal readonly TaskCompletionSource Disconnected = Signal();
        internal WebsocketSenderHandler Sender = null!;
        internal WebsocketConnectionHandler Connection = null!;
        internal readonly MemoryStream Stream = new();
        private readonly ConcurrentQueue<Task> _tasks = new();
        internal bool FailWrite;
        internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Event(string name) => Events.Enqueue(name);
        internal Task Track(Task task) { _tasks.Enqueue(task); return task; }
        internal void Initialize()
        {
            var tcp = new TcpConnectionService(() => false, (_, _, _, _) => true,
                (_, _) => Task.CompletedTask, (_, _) => { }, true, new TcpClient());
            // Install an established transport, as in ProtocolStateTests. Loopback tests
            // separately cover handshake/pipeline registration of the shared close task.
            typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tcp, Stream);
            Sender = new WebsocketSenderHandler(tcp, (_, _) => { }, async (_, bytes, count, _) =>
            {
                Frames.Enqueue(bytes.Take(count).ToArray());
                Event("write-entered");
                Entered.TrySetResult();
                await Wait(Resume.Task);
                if (FailWrite) { Event("write-failed"); throw new IOException("controlled partial write"); }
                Event("write-completed");
            }, false, async (duration, token) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(5), duration);
                Event("peer-wait");
                Waiting.TrySetResult();
                await Expire.Task.WaitAsync(token);
                Event("peer-timeout");
            });
            Connection = new WebsocketConnectionHandler(tcp, new WebsocketParserHandler(tcp), (status, _) =>
            {
                if (status == ConnectionStatus.Disconnected) { Event("disconnected"); Disconnected.TrySetResult(); }
            }, (_, _) => Sender);
            typeof(WebsocketConnectionHandler).GetField("_closeHandshakeOnce", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Connection, new Lazy<Task>(() => Connection.DisconnectWebsocket(Sender)));
        }
        internal async Task Wait(Task task)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException e) { throw new TimeoutException("Controlled schedule: " + string.Join(" -> ", Events), e); }
        }
        internal Task DisposeConnection() { Event("dispose-start"); return Track(Connection.DisposeAsync().AsTask()); }
        internal void AssertPending(Task disposal)
        {
            Assert.False(disposal.IsCompleted, "Disposal overtook paused work: " + string.Join(" -> ", Events));
            Assert.True(Stream.CanWrite, "Transport released before write completion");
            Assert.False(Disconnected.Task.IsCompleted);
        }
        public async ValueTask DisposeAsync()
        {
            Resume.TrySetResult(); Expire.TrySetResult(); Sender.NotifyReadFailure();
            try { await Wait(Task.WhenAll(_tasks.ToArray())); }
            catch (Exception e) { Event("cleanup: " + e.Message); }
            finally { Connection.Dispose(); output.WriteLine(string.Join(" -> ", Events)); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PausedCloseWriteBlocksDisposalAndPeerRaceSendsOnce(bool peerFirst)
    {
        await using var h = new Harness(output); h.Initialize();
        if (peerFirst) { h.Sender.NotifyPeerClose(); h.Event("peer-close-before-local"); }
        var local = h.Track(h.Sender.SendCloseHandshakeAsync(StatusCodes.GoingAway));
        await h.Wait(h.Entered.Task);
        if (!peerFirst) { h.Sender.NotifyPeerClose(); h.Event("peer-close-during-write"); }
        var disposal = h.DisposeConnection();
        var secondDisposal = h.DisposeConnection();
        h.AssertPending(disposal); h.AssertPending(secondDisposal);
        h.Resume.TrySetResult();
        await h.Wait(Task.WhenAll(local, disposal, secondDisposal));
        Assert.False(h.Stream.CanWrite);
        var frame = Assert.Single(h.Frames);
        Assert.Equal(0x88, frame[0]);
        Assert.True((frame[1] & 128) != 0);
        var payload = frame.Skip(6).Select((b, i) => (byte)(b ^ frame[2 + i % 4])).ToArray();
        Assert.Equal(new byte[] { 3, 233 }, payload.Take(2)); // GoingAway, network byte order
        Assert.Equal("GoingAway", System.Text.Encoding.UTF8.GetString(payload.Skip(2).ToArray()));
        Assert.True(h.Events.ToList().IndexOf("write-completed") < h.Events.ToList().IndexOf("disconnected"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeerWaitEndsByControlledTimeoutOrReadFailure(bool failure)
    {
        await using var h = new Harness(output); h.Initialize();
        var disposal = h.DisposeConnection();
        await h.Wait(h.Entered.Task); h.AssertPending(disposal);
        h.Resume.TrySetResult();
        await h.Wait(h.Waiting.Task); h.AssertPending(disposal);
        if (failure) { h.Event("read-failure"); h.Sender.NotifyReadFailure(); }
        else h.Expire.TrySetResult();
        await h.Wait(disposal);
        Assert.False(h.Stream.CanWrite); Assert.Single(h.Frames);
        Assert.True(h.Disconnected.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task FailedCloseWriteDisposesWithoutRetryOrPeerWait()
    {
        await using var h = new Harness(output); h.Initialize(); h.FailWrite = true;
        var disposal = h.DisposeConnection();
        await h.Wait(h.Entered.Task); h.AssertPending(disposal);
        h.Resume.TrySetResult(); await h.Wait(disposal);
        Assert.Single(h.Frames); Assert.False(h.Waiting.Task.IsCompleted); Assert.False(h.Stream.CanWrite);
    }

    [Fact]
    public async Task CancellationBetweenFragmentsAllowsOnlyClose()
    {
        await using var h = new Harness(output); h.Initialize();
        using var cancellation = new CancellationTokenSource();
        var send = h.Track(h.Sender.SendText(new[] { "first", "last" }, cancellation.Token));
        await h.Wait(h.Entered.Task);
        cancellation.Cancel(); h.Event("cancel-between-fragments"); h.Resume.TrySetResult();
        await h.Wait(Assert.ThrowsAnyAsync<OperationCanceledException>(() => send));
        await h.Wait(Assert.ThrowsAsync<WebsocketClientLiteException>(() => h.Sender.SendText("late")));
        h.Sender.NotifyPeerClose(); await h.Wait(h.DisposeConnection());
        Assert.Equal(new byte[] { 0x01, 0x88 }, h.Frames.Select(f => f[0]));
    }

    [Fact]
    public async Task QueuedPongIsSuppressedAfterPeerClose()
    {
        await using var h = new Harness(output); h.Initialize();
        var close = h.Track(h.Sender.SendCloseHandshakeAsync(StatusCodes.GoingAway));
        await h.Wait(h.Entered.Task);
        var pong = h.Track(h.Sender.SendPong(new Dataframe { Payload = new byte[] { 42 } }, CancellationToken.None));
        Assert.False(pong.IsCompleted); h.Event("pong-queued");
        h.Sender.NotifyPeerClose(); h.Event("peer-close"); h.Resume.TrySetResult();
        await h.Wait(Task.WhenAll(close, pong));
        await h.Wait(h.DisposeConnection()); Assert.Single(h.Frames);
    }

    [Fact]
    public async Task PostWriteFailureRejectsQueuedAndLaterDataAndSuppressesClose()
    {
        await using var h = new Harness(output); h.Initialize(); h.FailWrite = true;
        var first = h.Track(h.Sender.SendText("first")); await h.Wait(h.Entered.Task);
        var queued = h.Track(h.Sender.SendText("queued")); Assert.False(queued.IsCompleted);
        h.Resume.TrySetResult();
        await h.Wait(Assert.ThrowsAsync<WebsocketClientLiteException>(() => first));
        await h.Wait(Assert.ThrowsAsync<WebsocketClientLiteException>(() => queued));
        await h.Wait(Assert.ThrowsAsync<WebsocketClientLiteException>(() => h.Sender.SendText("later")));
        await h.Wait(h.DisposeConnection()); Assert.Single(h.Frames); Assert.False(h.Stream.CanWrite);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParserSignalsPeerCloseOrReadFailureBeforeTimerExpiry(bool readFailure)
    {
        var events = new ConcurrentQueue<string>();
        var peerWait = Harness.Signal();
        var sendPeer = Harness.Signal();
        var notification = Harness.Signal();
        var expire = Harness.Signal();
        int closeStatuses = 0;
        using var server = new LoopbackWebSocketServer();
        server.Start(async (stream, ct) =>
        {
            var close = await LoopbackWebSocketServer.ReadFrameAsync(stream, ct);
            Assert.Equal(8, close!.Value.opcode);
            await sendPeer.Task.WaitAsync(ct);
            await LoopbackWebSocketServer.WriteFrameAsync(stream, readFailure ? 1 : 8,
                readFailure ? new byte[64] : new byte[] { 3, 232 }, true, ct);
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });
        void Status(ConnectionStatus status, Exception? error)
        {
            events.Enqueue(status.ToString());
            if (status == ConnectionStatus.Close && Interlocked.Increment(ref closeStatuses) == 2 ||
                status == ConnectionStatus.Aborted) notification.TrySetResult();
        }
        using var tcp = new TcpConnectionService(() => false, (_, _, _, _) => true,
            (client, uri) => client.ConnectAsync(uri.Host, uri.Port), Status, true, maxFrameSize: 16);
        WebsocketSenderHandler? sender = null;
        var connection = new WebsocketConnectionHandler(tcp, new WebsocketParserHandler(tcp), Status,
            (_, callback) => sender = new WebsocketSenderHandler(tcp, callback,
                (stream, bytes, count, ct) => stream.WriteAsync(bytes, 0, count, ct), false,
                (_, _) => { peerWait.TrySetResult(); return expire.Task; }));
        IDisposable? subscription = null;
        async Task Wait(Task task)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException e) { throw new TimeoutException(string.Join(" -> ", events), e); }
        }
        try
        {
            var observable = await connection.ConnectWebsocket(server.Uri, null, System.Security.Authentication.SslProtocols.None,
                (_, _) => { }, false, default, null, TimeSpan.FromSeconds(10), null, null, null, CancellationToken.None);
            subscription = observable.Subscribe(_ => { }, _ => { });
            var disposal = connection.DisposeAsync().AsTask();
            await Wait(peerWait.Task);
            Assert.False(disposal.IsCompleted);
            sendPeer.TrySetResult();
            await Wait(notification.Task);
            // Status follows NotifyPeerClose/NotifyReadFailure synchronously. A new
            // waiter must therefore see readiness without expiring the held timer.
            var probe = sender!.WaitForPeerCloseAsync(TimeSpan.FromSeconds(5));
            Assert.True(probe.IsCompletedSuccessfully, "Parser notification did not release Close readiness");
            await Wait(disposal);
            Assert.False(expire.Task.IsCompleted);
        }
        finally
        {
            sendPeer.TrySetResult(); expire.TrySetResult();
            subscription?.Dispose();
            await Wait(connection.DisposeAsync().AsTask());
            output.WriteLine(string.Join(" -> ", events));
        }
    }

}
