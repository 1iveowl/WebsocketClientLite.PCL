using System;
using HttpMachine;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Parser
{
    internal class HttpParserDelegate : IHttpParserCombinedDelegate
    {
        private string _headerName;
        private bool _headerAlreadyExist;

        public readonly HttpWebsocketServerResponse HttpRequestReponse = new HttpWebsocketServerResponse();

        public void OnMessageBegin(HttpCombinedParser combinedParser)
        {
            //throw new NotImplementedException();
        }

        public void OnHeaderName(HttpCombinedParser combinedParser, string name)
        {
            // Header Field Names are case-insensitive http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
            if (HttpRequestReponse.Headers.ContainsKey(name.ToUpper()))
            {
                _headerAlreadyExist = true;
            }
            _headerName = name.ToUpper();
        }

        public void OnHeaderValue(HttpCombinedParser combinedParser, string value)
        {
            if (_headerAlreadyExist)
            {
                // Join multiple message-header fields into one comma seperated list http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
                HttpRequestReponse.Headers[_headerName] = $"{HttpRequestReponse.Headers[_headerName]}, {value}";
                _headerAlreadyExist = false;
            }
            else
            {
                HttpRequestReponse.Headers[_headerName] = value;
            }
        }

        public void OnHeadersEnd(HttpCombinedParser combinedParser)
        {
            //throw new NotImplementedException();
        }

        public void OnBody(HttpCombinedParser combinedParser, ArraySegment<byte> data)
        {
            //throw new NotImplementedException();
        }


        public void OnParserError()
        {
            HttpRequestReponse.IsUnableToParseHttp = true;
        }

        public MessageType MessageType { get; private set; }

        public void OnRequestType(HttpCombinedParser combinedParser)
        {
            HttpRequestReponse.MessageType = MessageType.Request;
            MessageType = MessageType.Request;
        }

        public void OnMethod(HttpCombinedParser combinedParser, string method)
        {
            //throw new NotImplementedException();
        }

        public void OnRequestUri(HttpCombinedParser combinedParser, string requestUri)
        {
            //throw new NotImplementedException();
        }

        public void OnPath(HttpCombinedParser combinedParser, string path)
        {
            //throw new NotImplementedException();
        }

        public void OnFragment(HttpCombinedParser combinedParser, string fragment)
        {
            //throw new NotImplementedException();
        }

        public void OnQueryString(HttpCombinedParser combinedParser, string queryString)
        {
            //throw new NotImplementedException();
        }

        public void OnResponseType(HttpCombinedParser combinedParser)
        {
            HttpRequestReponse.MessageType = MessageType.Response;
            MessageType = MessageType.Response;
        }

        public void OnResponseCode(HttpCombinedParser combinedParser, int statusCode, string statusReason)
        {
            HttpRequestReponse.StatusCode = statusCode;
            HttpRequestReponse.ResponseReason = statusReason;
        }

        public void OnMessageEnd(HttpCombinedParser combinedParser)
        {
            HttpRequestReponse.IsEndOfMessage = true;
        }

    }
}
