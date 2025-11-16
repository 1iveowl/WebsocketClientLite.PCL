using System;
using HttpMachine;
using System.Collections.Generic;
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
    WebsocketParserHandler websocketParserHandler,
    Action<ConnectionStatus, Exception?> connectionStatusAction)
{
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

            await SendHandshake(uri, sender, ct, origin, headers).ConfigureAwait(false);
            await WaitForHandshake(handshakeParser).ConfigureAwait(false);

            obs.OnCompleted();
        })
        .Timeout(timeout)
        .Catch<
            (HandshakeStateKind handshakeState, WebsocketClientLiteException? ex),
            TimeoutException>(
                tx => Observable.Return(
                    (HandshakeStateKind.HandshakeTimedOut,
                    new WebsocketClientLiteException("Handshake times out.", tx) ?? null)
                )
            );

        async Task WaitForHandshake(HandshakeParser handshakeParser)
        {
            bool isHandshakeDone;

            do
            {
                isHandshakeDone = await tcpConnectionService
                    .BytesObservable()
                    .Select(b => handshakeParser.Parse(b, subprotocols));
            } while (!isHandshakeDone);
        }
    }

    private async Task<(HandshakeStateKind handshakeState, WebsocketClientLiteException? ex)> 
        SendHandshake(
            Uri uri,
            WebsocketSenderHandler websocketSenderHandler,
            CancellationToken ct,
            string? origin = null,
            IDictionary<string, string>? headers = null)
    {
        try
        {
            connectionStatusAction(ConnectionStatus.SendingHandshakeToWebsocketServer, null);

            await websocketSenderHandler.SendConnectHandShake(
                     uri,
                     ct,
                     origin,
                     headers,
                     websocketParserHandler.SubprotocolAcceptedNames).ConfigureAwait(false);     
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
