using System;
using HttpMachine;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;
using WebsocketClientLite.Parser;
using IWebsocketClientLite;

namespace WebsocketClientLite.Service;

internal class HandshakeHandler(
    TcpConnectionService tcpConnectionService,
    Action<ConnectionStatus, Exception?> connectionStatusAction)
{
    /// <summary>
    /// Subprotocols the server accepted (intersection with the ones offered),
    /// available after a successful handshake.
    /// </summary>
    internal IEnumerable<string>? NegotiatedSubprotocols { get; private set; }

    internal IObservable<(HandshakeStateKind handshakeState, WebsocketClientLiteException? ex)> Handshake(
        Uri uri,
        WebsocketSenderHandler sender,
        TimeSpan timeout,
        CancellationToken ct,
        string? origin = null,
        IDictionary<string, string>? headers = null,
        IEnumerable<string>? subprotocols = null)
    {
        return Observable.Create<(HandshakeStateKind handshakeState, WebsocketClientLiteException? ex)>(async obs =>
        {
            using var parserDelegate = new HandshakeParserDelegate(obs);
            using var parserHandler = new HttpCombinedParser(parserDelegate);

            var handshakeParser = new HandshakeParser(
                parserHandler,
                parserDelegate,
                connectionStatusAction);

            var sendResult = await SendHandshake(uri, sender, ct, origin, headers, subprotocols).ConfigureAwait(false);

            if (sendResult.handshakeState is HandshakeStateKind.HandshakeSendFailed)
            {
                // No point waiting for a response to a request that never went out.
                obs.OnNext(sendResult);
                obs.OnCompleted();
                return;
            }

            await WaitForHandshake(handshakeParser).ConfigureAwait(false);

            NegotiatedSubprotocols = handshakeParser.SubprotocolAcceptedNames;

            obs.OnCompleted();
        })
        .Timeout(timeout)
        .Catch<
            (HandshakeStateKind handshakeState, WebsocketClientLiteException? ex),
            TimeoutException>(
                tx => Observable.Return<(HandshakeStateKind, WebsocketClientLiteException?)>(
                    (HandshakeStateKind.HandshakeTimedOut,
                    new WebsocketClientLiteException("Handshake timed out.", tx))
                )
            );

        async Task WaitForHandshake(HandshakeParser handshakeParser)
        {
            // Read the handshake response one byte at a time so the parser stops
            // exactly at the end of the HTTP response and does not consume bytes
            // belonging to the first WebSocket frame(s). The shared header
            // scratch buffer plus the span-based Execute keep the loop free of
            // per-byte allocations.
            while (true)
            {
                var bytes = await tcpConnectionService.ReadHeaderBytesAsync(1, ct).ConfigureAwait(false);

                if (bytes is null)
                {
                    throw new WebsocketClientLiteException(
                        "Connection closed before the WebSocket handshake completed.");
                }

                if (handshakeParser.Parse(bytes.AsSpan(0, 1), subprotocols))
                {
                    break;
                }
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Nothing is swallowed: the exception is converted into the HandshakeSendFailed result " +
                        "state (with the original exception preserved) which the connect path observes and throws. " +
                        "Any write-failure type must take this path, so the catch is intentionally broad.")]
    private async Task<(HandshakeStateKind handshakeState, WebsocketClientLiteException? ex)>
        SendHandshake(
            Uri uri,
            WebsocketSenderHandler websocketSenderHandler,
            CancellationToken ct,
            string? origin = null,
            IDictionary<string, string>? headers = null,
            IEnumerable<string>? subprotocols = null)
    {
        try
        {
            connectionStatusAction(ConnectionStatus.SendingHandshakeToWebsocketServer, null);

            await websocketSenderHandler.SendConnectHandShake(
                     uri,
                     ct,
                     origin,
                     headers,
                     subprotocols).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (
                HandshakeStateKind.HandshakeSendFailed, 
                new WebsocketClientLiteException("Handshake send failed.", ex)
            );
        }

        return (HandshakeStateKind.HandshakeSend, null);
    }
}
