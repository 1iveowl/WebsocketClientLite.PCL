using HttpMachine;
using IHttpMachine;
using System;
using WebsocketClientLite.PCL.CustomException;

namespace WebsocketClientLite.PCL.Parser
{
    internal class WebsocketHandshakeParserDelegate : HttpParserDelegate
    {
        private readonly IObserver<(HandshakeState handshakeState, WebsocketClientLiteException ex)> _observerHandshakeParserState;

        public WebsocketHandshakeParserDelegate(IObserver<(HandshakeState handshakeState, WebsocketClientLiteException ex)> observerHandshakeParserState)
        {
            _observerHandshakeParserState = observerHandshakeParserState;
        }

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
                _observerHandshakeParserState
                    .OnNext((HandshakeState.HandshakeCompletedSuccessfully, null));
            }
            else
            {
                _observerHandshakeParserState
                    .OnNext((HandshakeState.HandshakeFailed, null));
                _observerHandshakeParserState.OnError(new WebsocketClientLiteException("Unable to complete handshake"));
                return;
            }
            _observerHandshakeParserState.OnCompleted();
        }
    }
}
