using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HttpMachine;

namespace WebsocketClientLite.PCL.Parser
{
    class HandshakeParser
    {
        internal void Parse(byte[] data, HttpParserDelegate parserDelegate, HttpCombinedParser parserHandler)
        {
            try
            {
                if (parserHandler.Execute(new ArraySegment<byte>(data, 0, data.Length)) <= 0)
                {
                    parserDelegate.HttpRequestReponse.IsUnableToParseHttp = true;
                }
            }
            catch (Exception)
            {
                parserDelegate.HttpRequestReponse.IsUnableToParseHttp = true;
            }
        }
    }
}
