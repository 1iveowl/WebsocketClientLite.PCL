using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using WebsocketClientLite.Service;
using WebsocketClientLite.CustomException;
using System.Reactive.Linq;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite;
using Xunit;

namespace WebsocketClientLiteTest;

public class ReconnectTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedClientRejectsAdmission(bool asynchronous)
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(LoopbackWebSocketServer.EchoLoopAsync);
        var client = new ClientWebSocketRx();
        if (asynchronous) await client.DisposeAsync(); else client.Dispose();
        var terminal = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.WebsocketConnectWithStatusObservable(server.Uri).Subscribe(
            value => { if (value.state == ConnectionStatus.WebsocketConnected) terminal.TrySetResult(null); },
            error => terminal.TrySetResult(error));
        Assert.True(await terminal.Task.WaitAsync(Limit) is ObjectDisposedException,
            "Disposed client admitted a connection");
    }

    private static ConcurrentDictionary<WebsocketService, byte> Active(ClientWebSocketRx client) =>
        (ConcurrentDictionary<WebsocketService, byte>)typeof(ClientWebSocketRx)
            .GetField("_activeConnections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;

    [Fact]
    public async Task TerminalCallbackSuccessorSurvivesOldCleanup()
    {
        using var first = new LoopbackWebSocketServer();
        using var second = new LoopbackWebSocketServer();
        var endFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Start(async (stream, ct) =>
        {
            await endFirst.Task.WaitAsync(ct);
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 8, new byte[] { 3, 232 }, true, ct);
            await LoopbackWebSocketServer.ReadFrameAsync(stream, ct);
        });
        second.Start(LoopbackWebSocketServer.EchoLoopAsync);
        using var client = new ClientWebSocketRx();
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldTerminalReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var echo = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool connected = false;
        using var status = client.IsConnectedObservable.Subscribe(value => Volatile.Write(ref connected, value));
        IDisposable? successor = null;
        using var initial = client.WebsocketConnectWithStatusObservable(first.Uri).Subscribe(
            value => { if (value.state == ConnectionStatus.WebsocketConnected) firstReady.TrySetResult(); },
            ex => oldTerminalReturned.TrySetException(ex),
            () =>
            {
                successor = client.WebsocketConnectWithStatusObservable(second.Uri).Subscribe(value =>
                {
                    if (value.state == ConnectionStatus.WebsocketConnected) secondReady.TrySetResult();
                    if (value.state == ConnectionStatus.DataframeReceived) echo.TrySetResult(value.dataframe?.Message);
                }, ex => echo.TrySetException(ex));
                // Hold the old terminal callback until the successor handshake finishes.
                if (!secondReady.Task.Wait(Limit)) oldTerminalReturned.TrySetException(new TimeoutException());
                else oldTerminalReturned.TrySetResult();
            });
        try
        {
            await firstReady.Task.WaitAsync(Limit);
            var oldSender = client.Sender!;
            var oldService = Assert.Single(Active(client)).Key;
            endFirst.TrySetResult();
            await oldTerminalReturned.Task.WaitAsync(Limit);
            // Dispose the old subscription explicitly to join its Finally cleanup.
            initial.Dispose();
            var close = (Lazy<Task>)typeof(WebsocketConnectionHandler).GetField("_closeHandshakeOnce", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(oldService.WebsocketConnectionHandler)!;
            await close.Value.WaitAsync(Limit);
            Assert.True(!Active(client).ContainsKey(oldService), "Old service retained registration");
            Assert.Single(Active(client));
            Assert.NotSame(oldSender, client.Sender);
            Assert.True(Volatile.Read(ref connected), "Old cleanup reset successor connectivity");
            await Assert.ThrowsAsync<WebsocketClientLiteException>(() => oldSender.SendText("old"));
            await client.Sender!.SendText("new");
            Assert.Equal("new", await echo.Task.WaitAsync(Limit));
        }
        finally { successor?.Dispose(); }
        Assert.Empty(Active(client));
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("async")]
    [InlineData("unsubscribe")]
    public async Task SetupAdmissionIsOwnedUntilTeardown(string mode)
    {
        using var server = new LoopbackWebSocketServer();
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Start(LoopbackWebSocketServer.EchoLoopAsync, async (_, ct) =>
        {
            accepted.TrySetResult();
            await released.Task.WaitAsync(ct);
        });
        using var client = new ClientWebSocketRx();
        using var subscription = client.WebsocketConnectWithStatusObservable(server.Uri).Subscribe(_ => { }, _ => { });
        try
        {
            await accepted.Task.WaitAsync(Limit);
            Assert.True(Active(client).Count == 1, "Accepted setup missing registration");
            var service = Assert.Single(Active(client)).Key;
            var transport = (TcpConnectionService)typeof(WebsocketService)
                .GetField("_tcpConnectionService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            if (mode == "async") await client.DisposeAsync().AsTask().WaitAsync(Limit);
            else if (mode == "dispose") client.Dispose();
            else subscription.Dispose();
            Assert.Empty(Active(client));
            Assert.False(transport.ConnectionStream.CanRead);
            Assert.Null(client.Sender);
        }
        finally { released.TrySetResult(); }
    }

    private static TcpConnectionService Transport(WebsocketService service) =>
        (TcpConnectionService)typeof(WebsocketService)
            .GetField("_tcpConnectionService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;

    // Row 11 of verification/reconnect-decision.md: Register(g) after disposal (I1, I2). The
    // subscriber's Initialized callback runs between Subscribe(g), lock L1, and Register(g), the
    // Using resource factory, on the subscribing thread. It holds that thread until the test has
    // disposed the client, so disposal lands between the two. The server holds any handshake it
    // receives, so a service admitted by mistake cannot end, and leave the registry, before the
    // registry is read.
    [Theory]
    [InlineData("dispose")]
    [InlineData("async")]
    public async Task RegisterAfterDisposalAdmitsNoService(string mode)
    {
        using var server = new LoopbackWebSocketServer();
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Start(LoopbackWebSocketServer.EchoLoopAsync, async (_, ct) =>
        {
            accepted.TrySetResult();
            await released.Task.WaitAsync(ct);
        });
        var client = new ClientWebSocketRx();
        var values = new ConcurrentQueue<bool>();
        using var status = client.IsConnectedObservable.Subscribe(values.Enqueue);
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool held = true;
        IDisposable? subscription = null;
        try
        {
            var subscribing = Task.Run(() => subscription = client.WebsocketConnectWithStatusObservable(server.Uri).Subscribe(
                value =>
                {
                    if (value.state != ConnectionStatus.Initialized) return;
                    initialized.TrySetResult();
                    // Hold Subscribe between its admission (L1) and Register (L3) until disposal returns.
                    held = disposed.Task.Wait(Limit);
                },
                error => terminal.TrySetResult(error),
                () => terminal.TrySetResult(null)));
            await initialized.Task.WaitAsync(Limit);
            if (mode == "async") await client.DisposeAsync().AsTask().WaitAsync(Limit);
            else client.Dispose();
            disposed.TrySetResult();
            await subscribing.WaitAsync(Limit);
            Assert.True(held, "The schedule did not hold Subscribe until disposal returned");
            Assert.True(Active(client).IsEmpty, "Disposed client registered a service");
            Assert.True(terminal.Task.IsCompleted && terminal.Task.Result is ObjectDisposedException,
                "Setup after disposal was not rejected with ObjectDisposedException");
            Assert.False(accepted.Task.IsCompleted, "A connection after disposal reached the server");
            Assert.Null(client.Sender);
            Assert.DoesNotContain(true, values);
        }
        finally
        {
            disposed.TrySetResult();
            released.TrySetResult();
            subscription?.Dispose();
            client.Dispose();
        }
    }

    // Row 17 of verification/reconnect-decision.md: Connect(g) after disposal (I1, I2). The setup
    // was registered before disposal (row 8). WebsocketConnectionHandler reports
    // HandshakeCompletedSuccessfully on the handshake thread immediately before it calls
    // InitSender. The subscriber holds that callback until the test has disposed the client, so
    // disposal, and its drain of the registry, lands between the completed handshake and Connect.
    // The service's Dispose emits no status, so the first settlement after the release is the
    // handshake thread's own: the rejection, or WebsocketConnected after a wrongly installed sender.
    [Theory]
    [InlineData("dispose")]
    [InlineData("async")]
    public async Task ConnectAfterDisposalInstallsNoSender(string mode)
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(LoopbackWebSocketServer.EchoLoopAsync);
        var client = new ClientWebSocketRx();
        var values = new ConcurrentQueue<bool>();
        using var status = client.IsConnectedObservable.Subscribe(values.Enqueue);
        var handshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new ConcurrentQueue<ConnectionStatus>();
        Exception? error = null;
        bool held = true;
        var subscription = client.WebsocketConnectWithStatusObservable(server.Uri).Subscribe(
            value =>
            {
                statuses.Enqueue(value.state);
                if (value.state == ConnectionStatus.HandshakeCompletedSuccessfully)
                {
                    handshake.TrySetResult();
                    // Hold the handshake thread before InitSender until disposal returns.
                    held = disposed.Task.Wait(Limit);
                }
                if (value.state == ConnectionStatus.WebsocketConnected) settled.TrySetResult("WebsocketConnected");
            },
            ex => { error = ex; settled.TrySetResult("error " + ex.GetType().Name); },
            () => settled.TrySetResult("completed"));
        try
        {
            await handshake.Task.WaitAsync(Limit);
            var service = Assert.Single(Active(client)).Key;
            var transport = Transport(service);
            if (mode == "async") await client.DisposeAsync().AsTask().WaitAsync(Limit);
            else client.Dispose();
            Assert.True(Active(client).IsEmpty, "Disposal did not drain the setup in progress");
            disposed.TrySetResult();
            var outcome = await settled.Task.WaitAsync(Limit);
            Assert.True(held, "The schedule did not hold the handshake until disposal returned");
            Assert.True(client.Sender is null, $"Disposed client installed a sender (first settlement: {outcome})");
            Assert.False(statuses.Contains(ConnectionStatus.WebsocketConnected), "Disposed client reported WebsocketConnected");
            Assert.True(error is ObjectDisposedException, $"Connect after disposal was not rejected with ObjectDisposedException (first settlement: {outcome})");
            Assert.True(Active(client).IsEmpty, "Rejected setup left a registration");
            Assert.False(transport.ConnectionStream.CanRead, "Rejected setup kept its transport open");
            Assert.DoesNotContain(true, values);
        }
        finally
        {
            disposed.TrySetResult();
            subscription.Dispose();
            client.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SequentialRepeatOrRetryUsesFreshServices(bool retry)
    {
        using var first = new LoopbackWebSocketServer();
        using var second = new LoopbackWebSocketServer();
        var finishFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Start(async (stream, ct) =>
        {
            await finishFirst.Task.WaitAsync(ct);
            if (!retry)
            {
                await LoopbackWebSocketServer.WriteFrameAsync(stream, 8, new byte[] { 3, 232 }, true, ct);
                await LoopbackWebSocketServer.ReadFrameAsync(stream, ct);
            }
        });
        second.Start(LoopbackWebSocketServer.EchoLoopAsync);
        using var client = new ClientWebSocketRx();
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var echo = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        var source = Observable.Defer(() => client.WebsocketConnectWithStatusObservable(
            Interlocked.Increment(ref attempts) == 1 ? first.Uri : second.Uri));
        using var subscription = (retry ? source.Retry(2) : source.Repeat(2)).Subscribe(value =>
        {
            if (value.state == ConnectionStatus.WebsocketConnected)
            {
                if (Volatile.Read(ref attempts) == 1) firstReady.TrySetResult();
                else secondReady.TrySetResult();
            }
            if (value.state == ConnectionStatus.DataframeReceived) echo.TrySetResult(value.dataframe?.Message);
        }, ex => { secondReady.TrySetException(ex); echo.TrySetException(ex); });
        await firstReady.Task.WaitAsync(Limit);
        var oldSender = client.Sender!;
        var oldService = Assert.Single(Active(client)).Key;
        finishFirst.TrySetResult();
        await secondReady.Task.WaitAsync(Limit);
        Assert.Equal(2, attempts);
        Assert.NotSame(oldSender, client.Sender);
        Assert.True(!Active(client).ContainsKey(oldService), "Old service retained registration");
        await client.Sender!.SendText("successor");
        Assert.Equal("successor", await echo.Task.WaitAsync(Limit));
    }

    // Row 20 of verification/reconnect-decision.md: Terminate(g) for the current generation
    // reports not-connected (I6). The test's own Finally is the signal: Rx's Finally disposes its
    // source, and so runs the client's teardown Finally, before it runs its own action.
    [Theory]
    [InlineData("server-close")]
    [InlineData("unsubscribe")]
    public async Task EndedConnectionReportsNotConnected(string end)
    {
        using var server = new LoopbackWebSocketServer();
        var closeFromServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Start(async (stream, ct) =>
        {
            await closeFromServer.Task.WaitAsync(ct);
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 8, new byte[] { 3, 232 }, true, ct);
            await LoopbackWebSocketServer.ReadFrameAsync(stream, ct);
        });
        using var client = new ClientWebSocketRx();
        var values = new ConcurrentQueue<bool>();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var status = client.IsConnectedObservable.Subscribe(value =>
        {
            values.Enqueue(value);
            if (value) connected.TrySetResult();
        });
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = client.WebsocketConnectWithStatusObservable(server.Uri)
            .Finally(() => ended.TrySetResult())
            .Subscribe(_ => { }, _ => { });
        try
        {
            await connected.Task.WaitAsync(Limit);
            if (end == "unsubscribe") subscription.Dispose();
            else closeFromServer.TrySetResult();
            await ended.Task.WaitAsync(Limit);
            Assert.True(values.SequenceEqual(new[] { false, true, false }),
                $"IsConnected did not become false when the connection ended ({end}): {string.Join(", ", values)}");
        }
        finally
        {
            subscription.Dispose();
            closeFromServer.TrySetResult();
        }
    }

    [Fact]
    public async Task FailedConnectTerminatesAndRemovesRegistration()
    {
        using var server = new LoopbackWebSocketServer();
        server.StartWithRawHandshakeResponse("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n");
        using var client = new ClientWebSocketRx();
        var terminal = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.WebsocketConnectWithStatusObservable(server.Uri).Subscribe(_ => { }, ex => terminal.TrySetResult(ex));
        await terminal.Task.WaitAsync(Limit);
        subscription.Dispose();
        Assert.True(SpinWait.SpinUntil(() => Active(client).IsEmpty, Limit), "Failed setup retained registration");
        Assert.Null(client.Sender);
    }
}
