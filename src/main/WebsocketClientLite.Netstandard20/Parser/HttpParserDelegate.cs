using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using HttpMachine;
using IHttpMachine;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Parser
{
    internal class HttpParserDelegate : IHttpParserCombinedDelegate
    {
        private string _headerName;
        private bool _headerAlreadyExist;

        private readonly IObserver<ParserState> _observerHandshakeParserState;

        internal IObservable<ParserState> HandshakeParserCompletionObservable { get; }
        

        internal HttpParserDelegate()
        {
            var handshakeParserStateSubject = new BehaviorSubject<ParserState>(ParserState.Start);

            _observerHandshakeParserState = handshakeParserStateSubject.AsObserver();
            HandshakeParserCompletionObservable = handshakeParserStateSubject.AsObservable();

        }

        internal readonly HttpWebsocketServerResponse HttpRequestResponse = new HttpWebsocketServerResponse();

        public void OnMessageBegin(IHttpCombinedParser combinedParser)
        {
            //throw new NotImplementedException();
        }

        public void OnHeaderName(IHttpCombinedParser combinedParser, string name)
        {
            // Header Field Names are case-insensitive http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
            if (HttpRequestResponse.Headers.ContainsKey(name.ToUpper()))
            {
                _headerAlreadyExist = true;
            }
            _headerName = name.ToUpper();
        }

        public void OnHeaderValue(IHttpCombinedParser combinedParser, string value)
        {
            if (_headerAlreadyExist)
            {
                // Join multiple message-header fields into one comma seperated list http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
                HttpRequestResponse.Headers[_headerName] = $"{HttpRequestResponse.Headers[_headerName]}, {value}";
                _headerAlreadyExist = false;
            }
            else
            {
                HttpRequestResponse.Headers[_headerName] = value;
            }
        }

        public void OnHeadersEnd(IHttpCombinedParser combinedParser)
        {
            //throw new NotImplementedException();
        }

        public void OnBody(IHttpCombinedParser combinedParser, ArraySegment<byte> data)
        {
            //throw new NotImplementedException();
        }


        public void OnParserError()
        {
            HttpRequestResponse.IsUnableToParseHttp = true;
        }

        public MessageType MessageType { get; private set; }

        public void OnRequestType(IHttpCombinedParser combinedParser)
        {
            HttpRequestResponse.MessageType = MessageType.Request;
            MessageType = MessageType.Request;
        }

        public void OnMethod(IHttpCombinedParser combinedParser, string method)
        {
            //throw new NotImplementedException();
        }

        public void OnRequestUri(IHttpCombinedParser combinedParser, string requestUri)
        {
            //throw new NotImplementedException();
        }

        public void OnPath(IHttpCombinedParser combinedParser, string path)
        {
            //throw new NotImplementedException();
        }

        public void OnFragment(IHttpCombinedParser combinedParser, string fragment)
        {
            //throw new NotImplementedException();
        }

        public void OnQueryString(IHttpCombinedParser combinedParser, string queryString)
        {
            //throw new NotImplementedException();
        }

        public void OnResponseType(IHttpCombinedParser combinedParser)
        {
            HttpRequestResponse.MessageType = MessageType.Response;
            MessageType = MessageType.Response;
        }

        public void OnResponseCode(IHttpCombinedParser combinedParser, int statusCode, string statusReason)
        {
            HttpRequestResponse.StatusCode = statusCode;
            HttpRequestResponse.ResponseReason = statusReason;
        }

        public void OnTransferEncodingChunked(IHttpCombinedParser combinedParser, bool isChunked)
        {

        }

        public void OnChunkedLength(IHttpCombinedParser combinedParser, int length)
        {

        }

        public void OnChunkReceived(IHttpCombinedParser combinedParser)
        {

        }

        public void OnMessageEnd(IHttpCombinedParser combinedParser)
        {
            HttpRequestResponse.IsEndOfMessage = true;

            if (!HttpRequestResponse.IsRequestTimedOut && !HttpRequestResponse.IsUnableToParseHttp)
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

        public void Dispose()
        {

        }
    }
}
