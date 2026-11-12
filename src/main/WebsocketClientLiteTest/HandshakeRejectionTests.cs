using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Service;
using Xunit;

namespace WebsocketClientLiteTest;

// A handshake that reports an error must end ConnectWebsocket with that error and never
// install the sender. The test awaits the connect call itself, so it sees the outcome
// directly instead of racing the connect code for Sender (lesson 10 of
// docs/pdd-10.3-session-plan.md). Through the public client, the 404 and invalid-accept
// rejections also arrive as an Aborted status before the connect code reaches its throw,
// which is why the message tests in IntegrationTests cannot see a missing throw.
// ReconnectTests.FailedConnectTerminatesAndRemovesRegistration checks Sender through the
// public client and still races against it.
public class HandshakeRejectionTests
{
    public enum Rejection
    {
        // HTTP 404: the parser reports HandshakeFailed and an Aborted status.
        NotFound,
        // 101 with a wrong Sec-WebSocket-Accept: the same two reports.
        InvalidAccept,
        // No response within the handshake timeout: HandshakeTimedOut, no status.
        Timeout,
        // The request cannot be written: HandshakeSendFailed, no status.
        SendFailed,
    }

    [Theory]
    [InlineData(Rejection.NotFound)]
    [InlineData(Rejection.InvalidAccept)]
    [InlineData(Rejection.Timeout)]
    [InlineData(Rejection.SendFailed)]
    public async Task RejectedHandshakeThrowsAndNeverInstallsSender(Rejection rejection)
    {
        var injected = new InvalidOperationException("Injected handshake write failure.");
        var statuses = new ConcurrentQueue<(ConnectionStatus status, Exception? error)>();
        void Status(ConnectionStatus status, Exception? error) => statuses.Enqueue((status, error));

        // The test connects the TCP client itself before ConnectWebsocket, which then skips
        // its own connect. ConnectWebsocket's timeout bounds both the TCP connect and the
        // handshake, so a slow connect on a loaded host would otherwise end the Timeout row
        // with a TCP connect error before the handshake runs. Here the timeout bounds only
        // the handshake.
        var client = new TcpClient(AddressFamily.InterNetwork);
        var tcp = new TcpConnectionService(() => false, (_, _, _, _) => true,
            (_, _) => Task.FromException(new InvalidOperationException("The test connects the TCP client before ConnectWebsocket.")),
            Status, true, client);
        // Declared before the server, so it is disposed after it. Only a connect that went on
        // past the throw (the mutant) leaves a close handshake for DisposeAsync to run. With the
        // server gone and no peer-close wait, that close ends at once instead of waiting out
        // its timeouts.
        await using var connection = new WebsocketConnectionHandler(tcp, new WebsocketParserHandler(tcp), Status,
            (_, callback) => new WebsocketSenderHandler(tcp, callback, async (stream, bytes, count, ct) =>
            {
                if (rejection == Rejection.SendFailed) throw injected;
                await stream.WriteAsync(bytes.AsMemory(0, count), ct);
                await stream.FlushAsync(ct);
            }, false, (_, _) => Task.CompletedTask));

        using var server = new LoopbackWebSocketServer();
        switch (rejection)
        {
            case Rejection.NotFound:
                server.StartWithRawHandshakeResponse("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n");
                break;
            case Rejection.InvalidAccept:
                server.StartWithRawHandshakeResponse("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\n" +
                    "Connection: Upgrade\r\nSec-WebSocket-Accept: wrong\r\n\r\n");
                break;
            default:
                // Reads the request, if one arrives, and never answers it.
                server.Start((_, ct) => Task.Delay(Timeout.Infinite, ct), (_, ct) => Task.Delay(Timeout.Infinite, ct));
                break;
        }
        await client.ConnectAsync(server.Uri.Host, server.Uri.Port).WaitAsync(TimeSpan.FromSeconds(30));

        var senderCalls = 0;
        // The handshake timeout is the only bound on the call, and it ends the Timeout row
        // whatever the load: a slow request write ends in the same HandshakeTimedOut. The other
        // rows must fail on their own cause, so their bound is generous.
        var handshakeTimeout = rejection == Rejection.Timeout ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(30);
        var error = await Record.ExceptionAsync(() => connection.ConnectWebsocket(
            server.Uri, null, SslProtocols.None, (_, _) => Interlocked.Increment(ref senderCalls),
            false, default, null, handshakeTimeout, null, null, null, CancellationToken.None));

        // Awaited directly, with no outer time limit: ConnectWebsocket has returned or thrown,
        // so no later call can come from it.
        Assert.Equal(0, Volatile.Read(ref senderCalls));
        var thrown = Assert.IsType<WebsocketClientLiteException>(error);
        var reported = statuses.ToArray();
        Assert.DoesNotContain(reported, s => s.status is ConnectionStatus.HandshakeCompletedSuccessfully or ConnectionStatus.WebsocketConnected);
        var aborted = reported.Where(s => s.status == ConnectionStatus.Aborted).Select(s => s.error).ToArray();

        switch (rejection)
        {
            case Rejection.NotFound:
            case Rejection.InvalidAccept:
                // The connect call throws the rejection the status reported, not another error.
                // The two are separate instances and the exception has no status code, so the
                // check compares their messages with each other, never with production text.
                var status = Assert.Single(aborted);
                Assert.Equal(Assert.IsType<WebsocketClientLiteException>(status).Message, thrown.Message);
                break;
            case Rejection.Timeout:
                Assert.Empty(aborted);
                Assert.IsType<TimeoutException>(thrown.InnerException);
                break;
            case Rejection.SendFailed:
                Assert.Empty(aborted);
                Assert.Contains(Causes(thrown), e => ReferenceEquals(e, injected));
                break;
        }
    }

    private static IEnumerable<Exception> Causes(Exception error)
    {
        for (var e = error.InnerException; e is not null; e = e.InnerException) yield return e;
    }
}
