using HttpMachine;
using IHttpMachine;
using System;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Parser
{
    internal class HandshakeParserDelegate : HttpParserDelegate
    {
        private readonly IObserver<(HandshakeStateKind handshakeState, WebsocketClientLiteException ex)> _observerHandshakeParserState;
       
        public HandshakeParserDelegate(
            IObserver<(HandshakeStateKind handshakeState, 
            WebsocketClientLiteException ex)> observerHandshakeParserState)
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
                    .OnNext((HandshakeStateKind.HandshakeCompletedSuccessfully, null));
            }
            else
            {
                _observerHandshakeParserState
                    .OnNext((HandshakeStateKind.HandshakeFailed, null));
                _observerHandshakeParserState.OnError(new WebsocketClientLiteException("Unable to complete handshake"));
                return;
            }
        }
    }
}
