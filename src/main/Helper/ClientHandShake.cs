using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebsocketClientLite.PCL.Helper
{
    internal static class ClientHandShake
    {
        internal static byte[] Compose(Uri uri, bool isSecure, string origin = null, IEnumerable<string> subprotocols = null)
        {
            var sb = new StringBuilder();

            sb.Append($"GET {uri.PathAndQuery} HTTP/1.1\r\n");
            //sb.Append($"GET {uri.AbsoluteUri} HTTP/1.1\r\n");
            sb.Append($"Host: {uri.Host}\r\n");
            sb.Append($"Upgrade: websocket\r\n");
            sb.Append($"Connection: Upgrade\r\n");
            //sb.Append("Pragma: no-cache\r\n");
            //sb.Append("Cache-Control: no-cache\r\n");

            if (!string.IsNullOrEmpty(origin))
            {
                sb.Append($"Origin: {origin}\r\n");
            }

            sb.Append($"Sec-WebSocket-Key: {GenerateRandomWebSocketKey()}\r\n");

            if (subprotocols == null)
            {
                //sb.Append($"Sec-WebSocket-Protocol: chat, superchat\r\n");
            }
            else
            {
                var subprotocolHeader = "";// = "Sec-WebSocket-Protocol: chat, superchat";

                foreach (var protocol in subprotocols)
                {
                    subprotocolHeader = $"{subprotocolHeader}, {protocol}";
                }
                sb.Append($"{subprotocolHeader}\r\n");
            }
            
            sb.Append($"Sec-WebSocket-Version: 13\r\n");
            sb.Append($"\r\n");

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static string GenerateRandomWebSocketKey()
        {
            var webSocketKey = new byte[16];
            var rnd = new Random();
            rnd.NextBytes(webSocketKey);
            return Convert.ToBase64String(webSocketKey);
        }
    }
}
