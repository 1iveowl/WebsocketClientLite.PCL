using System;
using HttpMachine;

namespace WebsocketClientLite.PCL.Parser
{
    internal class HandshakeParser
    {
        internal void Parse(byte[] data, HttpParserDelegate parserDelegate, HttpCombinedParser parserHandler)
        {
            try
            {
                if (parserHandler.Execute(new ArraySegment<byte>(data, 0, data.Length)) <= 0)
                {
                    parserDelegate.HttpRequestResponse.IsUnableToParseHttp = true;
                }
            }
            catch (Exception)
            {
                parserDelegate.HttpRequestResponse.IsUnableToParseHttp = true;
            }
        }
    }
}
