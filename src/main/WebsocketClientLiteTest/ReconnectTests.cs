using System;
using System.Collections.Concurrent;
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
