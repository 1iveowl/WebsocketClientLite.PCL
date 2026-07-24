using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite;
using Xunit;

namespace WebsocketClientLiteTest;

/// <summary>
/// End-to-end tests for the connection-teardown paths: the close handshake must
/// run exactly once on every end path (completion, error, unsubscribe, Dispose,
/// DisposeAsync), the close frame must go out before the socket is torn down,
/// and a read-loop error must reach the subscriber unreplaced. These are the
/// regression tests for the former FinallyAsync operator's blind spots.
/// </summary>
public class TeardownTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static (Task<int> closeFrameCount, Func<Stream, CancellationToken, Task> behavior)
        CountingCloseBehavior(Func<Stream, CancellationToken, Task>? beforeCounting = null)
    {
        // Counts every Close frame the client sends until the client tears the
        // TCP connection down (EOF), so "exactly once" is asserted against the
        // full lifetime of the connection, not a single read.
        var closeFrames = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        return (closeFrames.Task, async (stream, ct) =>
        {
            if (beforeCounting is not null)
            {
                await beforeCounting(stream, ct).ConfigureAwait(false);
            }

            int count = 0;
            try
            {
                while (true)
                {
                    var frame = await LoopbackWebSocketServer.ReadFrameAsync(stream, ct).ConfigureAwait(false);
                    if (frame is null)
                    {
                        break; // client closed the TCP connection
                    }

                    if (frame.Value.opcode == 0x8)
                    {
                        count++;
                    }
                }
            }
            catch (IOException)
            {
                // Abrupt client-side teardown still ends the count.
            }

            closeFrames.TrySetResult(count);
        });
    }

    private static async Task<(IDisposable subscription, TaskCompletionSource completed, TaskCompletionSource<Exception> errored)>
        ConnectAsync(ClientWebSocketRx client, Uri uri)
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errored = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscription = client.WebsocketConnectWithStatusObservable(uri)
            .Subscribe(
                tuple =>
                {
                    if (tuple.state == ConnectionStatus.WebsocketConnected)
                    {
                        connected.TrySetResult();
                    }
                },
                ex => errored.TrySetResult(ex),
                () => completed.TrySetResult());

        await connected.Task.WaitAsync(Timeout);
        return (subscription, completed, errored);
    }

    [Fact]
    public async Task ServerInitiatedClose_RacingSubscriptionDisposal_SendsExactlyOneCloseFrame()
    {
        using var server = new LoopbackWebSocketServer();
        var (closeFrameCount, behavior) = CountingCloseBehavior(async (stream, ct) =>
            // Server initiates the close immediately; the client's completion
            // teardown then races the subscriber disposing on completion.
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 0x8, new byte[] { 0x03, 0xE8 }, true, ct));
        server.Start(behavior);

        using var client = new ClientWebSocketRx();
        var (subscription, completed, _) = await ConnectAsync(client, server.Uri);

        await completed.Task.WaitAsync(Timeout);

        // Dispose right on the heels of completion — the exact race between the
        // pipeline's close path and the disposal path.
        subscription.Dispose();

        Assert.Equal(1, await closeFrameCount.WaitAsync(Timeout));
    }

    [Fact]
    public async Task UnsubscribeMidStream_SendsExactlyOneCloseFrame_BeforeSocketTeardown()
    {
        using var server = new LoopbackWebSocketServer();
        var (closeFrameCount, behavior) = CountingCloseBehavior();
        server.Start(behavior);

        using var client = new ClientWebSocketRx();
        var (subscription, _, _) = await ConnectAsync(client, server.Uri);

        // Unsubscribing mid-stream is how consumers leave the connection. The
        // count reaching the server proves the close frame went out before EOF
        // (frames and EOF are observed sequentially on the same stream).
        subscription.Dispose();

        Assert.Equal(1, await closeFrameCount.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ReadLoopError_ReachesSubscriber_NotReplacedByTeardown()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(async (stream, ct) =>
        {
            // An oversized frame (> client MaxFrameSize) forces a read-loop
            // error. The connection is held open so the only terminal event is
            // the client-side error — neither the teardown's close handshake
            // nor the Disconnected status may replace it with a completion.
            await LoopbackWebSocketServer.WriteFrameAsync(stream, 0x1, new byte[64], true, ct);
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        using var client = new ClientWebSocketRx { MaxFrameSize = 16 };
        var (subscription, completed, errored) = await ConnectAsync(client, server.Uri);
        using var _ = subscription;

        var winner = await Task.WhenAny(errored.Task, completed.Task).WaitAsync(Timeout);
        Assert.True(winner == errored.Task, "stream completed gracefully instead of surfacing the read-loop error");

        var error = await errored.Task;
        Assert.Contains("exceeds the configured maximum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AbruptServerDrop_SurfacesReadError_DespiteFailingCloseSend()
    {
        using var server = new LoopbackWebSocketServer();
        server.Start((stream, ct) =>
            // Drop the TCP connection right after the handshake: the read loop
            // hits an unexpected EOF (error path) and the teardown's courtesy
            // close-frame send fails on the dead socket. That cleanup failure
            // must not replace the original read error reaching the subscriber.
            Task.CompletedTask);

        using var client = new ClientWebSocketRx();
        var (subscription, completed, errored) = await ConnectAsync(client, server.Uri);
        using var _ = subscription;

        var winner = await Task.WhenAny(errored.Task, completed.Task).WaitAsync(Timeout);
        Assert.True(winner == errored.Task, "stream completed gracefully instead of surfacing the read error");

        var error = await errored.Task;
        Assert.Contains("aborted unexpectedly", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisposeAsync_AwaitsCloseHandshake_AndCompletesSubscriber()
    {
        using var server = new LoopbackWebSocketServer();
        var (closeFrameCount, behavior) = CountingCloseBehavior();
        server.Start(behavior);

        var client = new ClientWebSocketRx();
        var (subscription, completed, _) = await ConnectAsync(client, server.Uri);
        using var _ = subscription;

        // Graceful teardown: when DisposeAsync returns, the close handshake has
        // been awaited — the close frame is already on the wire.
        await client.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.Equal(1, await closeFrameCount.WaitAsync(Timeout));

        // The subscriber's stream ends gracefully (Disconnected + completion),
        // not with an error.
        await completed.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Dispose_IsBounded_AndStillAttemptsCloseHandshake()
    {
        using var server = new LoopbackWebSocketServer();
        var (closeFrameCount, behavior) = CountingCloseBehavior();
        server.Start(behavior);

        var client = new ClientWebSocketRx();
        var (subscription, _, _) = await ConnectAsync(client, server.Uri);
        using var _ = subscription;

        // Abrupt teardown: Dispose must return within its bound (3 s close wait
        // + margin; near-instant against a live loopback socket) and still give
        // the close frame its best-effort chance.
        var stopwatch = Stopwatch.StartNew();
        client.Dispose();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Dispose took {stopwatch.Elapsed} — not bounded");
        Assert.Equal(1, await closeFrameCount.WaitAsync(Timeout));
    }

    [Fact]
    public async Task AwaitUsing_ClosesGracefully()
    {
        using var server = new LoopbackWebSocketServer();
        var (closeFrameCount, behavior) = CountingCloseBehavior();
        server.Start(behavior);

        IDisposable? subscription = null;
        await using (var client = new ClientWebSocketRx())
        {
            var (sub, _, _) = await ConnectAsync(client, server.Uri);
            subscription = sub;
        }

        subscription.Dispose();

        Assert.Equal(1, await closeFrameCount.WaitAsync(Timeout));
    }
}
