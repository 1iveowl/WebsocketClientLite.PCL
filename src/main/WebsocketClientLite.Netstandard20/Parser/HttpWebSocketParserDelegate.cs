using HttpMachine;
using IHttpMachine;
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace WebsocketClientLite.PCL.Parser
{
    internal class HttpWebSocketParserDelegate : HttpParserDelegate
    {
        private readonly IObserver<ParserState> _observerHandshakeParserState;
        internal IObservable<ParserState> HandshakeParserCompletionObservable { get; }

        public HttpWebSocketParserDelegate()
        {
            var handshakeParserStateSubject = new BehaviorSubject<ParserState>(ParserState.Start);

            _observerHandshakeParserState = handshakeParserStateSubject.AsObserver();
            HandshakeParserCompletionObservable = handshakeParserStateSubject.AsObservable();
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
                _observerHandshakeParserState.OnNext(ParserState.HandshakeCompletedSuccessfully);
            }
            else
            {
                _observerHandshakeParserState.OnNext(ParserState.HandshakeFailed);
                _observerHandshakeParserState.OnError(new Exception("Unable to complete handshake"));
                return;
            }

            _observerHandshakeParserState.OnCompleted();
        }
    }
}
