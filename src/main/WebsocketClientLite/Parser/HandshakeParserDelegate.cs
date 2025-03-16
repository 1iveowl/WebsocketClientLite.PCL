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

        if (HttpRequestResponse.IsEndOfMessage)
        {
            observerHandshakeParserState
                .OnNext((HandshakeStateKind.HandshakeCompletedSuccessfully, null));
        }
        else
        {
            observerHandshakeParserState
                .OnNext((HandshakeStateKind.HandshakeFailed, null));
            observerHandshakeParserState.OnError(new WebsocketClientLiteException("Unable to complete handshake"));
            return;
        }
    }
}
