using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite;
using Xunit;

namespace WebsocketClientLiteTest;

public class IntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // Drives a real ClientWebSocketRx against the loopback server and surfaces
    // connection state, received messages, and errors as awaitable primitives.
    private sealed class TestClient : IDisposable
    {
        private readonly IDisposable _subscription;
        private readonly CancellationTokenSource _cts = new();

        public ClientWebSocketRx Client { get; }
        public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Exception> Errored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<string> Messages { get; } = Channel.CreateUnbounded<string>();

        public TestClient(Uri uri, bool hasClientPing = false)
        {
            Client = new ClientWebSocketRx();

            _subscription = Client.WebsocketConnectWithStatusObservable(
                    uri,
                    hasClientPing: hasClientPing,
                    clientPingInterval: TimeSpan.FromMilliseconds(250),
                    clientPingMessage: "ping",
                    cancellationToken: _cts.Token)
                .Subscribe(
                    tuple =>
                    {
                        switch (tuple.state)
                        {
                            case ConnectionStatus.WebsocketConnected:
                                Connected.TrySetResult();
                                break;
                            case ConnectionStatus.DataframeReceived when tuple.dataframe?.Message is { } message:
                                Messages.Writer.TryWrite(message);
                                break;
                        }
                    },
                    ex => Errored.TrySetResult(ex));
        }

        public async Task<string> NextMessageAsync() =>
            await Messages.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public void Dispose()
        {
            _subscription.Dispose();
            _cts.Cancel();
            _cts.Dispose();
            Client.Dispose();
        }
    }

    [Fact]
    public async Task Connects_HandshakesAndEchoesTextMessage()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(LoopbackWebSocketServer.EchoLoopAsync);

        using var client = new TestClient(server.Uri);
        await client.Connected.Task.WaitAsync(Timeout);

        await client.Client.Sender!.SendText("hello world");

        Assert.Equal("hello world", await client.NextMessageAsync());
    }

    [Fact]
    public async Task Echoes_LargeMessage_ExtendedLength()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(LoopbackWebSocketServer.EchoLoopAsync);

        using var client = new TestClient(server.Uri);
        await client.Connected.Task.WaitAsync(Timeout);

        var large = new string('x', 70000); // forces 64-bit extended length on send and receive
        await client.Client.Sender!.SendText(large);

        Assert.Equal(large, await client.NextMessageAsync());
    }

    [Fact]
    public async Task Echoes_MultipleMessages_InOrder()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(LoopbackWebSocketServer.EchoLoopAsync);

        using var client = new TestClient(server.Uri);
        await client.Connected.Task.WaitAsync(Timeout);

        for (int i = 0; i < 20; i++)
        {
            await client.Client.Sender!.SendText($"msg-{i}");
        }

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal($"msg-{i}", await client.NextMessageAsync());
        }
    }

    [Fact]
    public async Task Reassembles_ServerFragmentedMessage_EndToEnd()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(async (stream, ct) =>
        {
            // Text(FIN=0) + Continuation(FIN=0) + Continuation(FIN=1).
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 0x1, Encoding.UTF8.GetBytes("Hello, "), false, ct);
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 0x0, Encoding.UTF8.GetBytes("frag"), false, ct);
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 0x0, Encoding.UTF8.GetBytes("mented!"), true, ct);
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        using var client = new TestClient(server.Uri);
        await client.Connected.Task.WaitAsync(Timeout);

        Assert.Equal("Hello, fragmented!", await client.NextMessageAsync());
    }

    [Fact]
    public async Task ServerPing_IsAnsweredWithClientPong()
    {
        using var server = new LoopbackWebSocketServer();
        var pongReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        server.Start(async (stream, ct) =>
        {
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 0x9, Encoding.UTF8.GetBytes("ping?"), true, ct);

            // The client must answer with a Pong (opcode 0xA).
            var frame = await LoopbackWebSocketServer.ReadFrameAsync(stream, ct);
            pongReceived.TrySetResult(frame is { opcode: 0xA });

            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        using var client = new TestClient(server.Uri);
        await client.Connected.Task.WaitAsync(Timeout);

        Assert.True(await pongReceived.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task DisposingClientBeforeSubscription_DoesNotThrowFromTeardown()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(LoopbackWebSocketServer.EchoLoopAsync);

        var client = new ClientWebSocketRx();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscription = client.WebsocketConnectWithStatusObservable(server.Uri)
            .Subscribe(tuple =>
            {
                if (tuple.state == ConnectionStatus.WebsocketConnected)
                {
                    connected.TrySetResult();
                }
            },
            _ => { });

        await connected.Task.WaitAsync(Timeout);

        // Dispose the client FIRST (disposing the IsConnected subject), then the
        // subscription. Teardown's Finally reports IsConnected=false on the
        // disposed subject — this must not throw.
        client.Dispose();
        subscription.Dispose();
    }

    [Fact]
    public async Task ClientPing_KeepsConnectionAlive_WhileSending()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(LoopbackWebSocketServer.EchoLoopAsync);

        // Client pings every 250ms; send a burst of messages so sends and pings
        // interleave on the same stream (guards write serialization end-to-end).
        using var client = new TestClient(server.Uri, hasClientPing: true);
        await client.Connected.Task.WaitAsync(Timeout);

        const int count = 30;
        for (int i = 0; i < count; i++)
        {
            await client.Client.Sender!.SendText($"m{i}");
            await Task.Delay(20);
        }

        var received = Enumerable.Range(0, count).Select(_ => client.NextMessageAsync().Result).ToList();
        Assert.Equal(Enumerable.Range(0, count).Select(i => $"m{i}"), received);
        Assert.False(client.Errored.Task.IsCompleted, "connection errored during ping/send interleaving");
    }
}
