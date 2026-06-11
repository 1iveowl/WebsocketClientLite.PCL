using HttpMachine;
using IHttpMachine;
using System;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;

namespace WebsocketClientLite.Parser;

internal class HandshakeParserDelegate(
    IObserver<(
            HandshakeStateKind handshakeState,
            WebsocketClientLiteException? ex)> observerHandshakeParserState) : HttpParserDelegate
{
    public override void OnMessageBegin(IHttpCombinedParser combinedParser)
    {
        base.OnMessageBegin(combinedParser);
    }

    public override void OnHeadersEnd(IHttpCombinedParser combinedParser)
    {
        base.OnHeadersEnd(combinedParser);
    }

    public override void OnMessageEnd(IHttpCombinedParser combinedParser)
    {
        base.OnMessageEnd(combinedParser);

        if (!HttpRequestResponse.IsEndOfMessage)
        {
            // Defensive: should not happen — OnMessageEnd implies a complete message.
            observerHandshakeParserState
                .OnNext((HandshakeStateKind.HandshakeFailed, null));
            observerHandshakeParserState.OnError(new WebsocketClientLiteException("Unable to complete handshake"));
            return;
        }

        // Only "101 Switching Protocols" is a successful WebSocket upgrade. Any
        // other complete response is a failed handshake — report it as such so
        // the connect path can fail fast with the server's status line.
        if (HttpRequestResponse.StatusCode == 101)
        {
            observerHandshakeParserState
                .OnNext((HandshakeStateKind.HandshakeCompletedSuccessfully, null));
        }
        else
        {
            observerHandshakeParserState.OnNext((
                HandshakeStateKind.HandshakeFailed,
                new WebsocketClientLiteException(
                    $"Unable to connect to websocket server. " +
                    $"HTTP status: {HttpRequestResponse.StatusCode}, " +
                    $"reason: {HttpRequestResponse.ResponseReason}")));
        }
    }
}
