using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WebsocketClientLite.PCL.Helper
{
    internal static class ClientHandShake
    {
        internal static byte[] Compose(
            Uri uri,
            string origin = null, 
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null,
            bool isSocketIOv4 = false)
        {
            var sb = new StringBuilder();

            sb.Append($"GET {uri.PathAndQuery} HTTP/1.1\r\n");
            sb.Append($"Host: {uri.Host}\r\n");
            sb.Append($"Upgrade: websocket\r\n");
            sb.Append($"Connection: Upgrade\r\n");

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    sb.Append($"{header.Key}: {header.Value}\r\n");
                }
            }

            if (!string.IsNullOrEmpty(origin))
            {
                sb.Append($"Origin: {origin}\r\n");
            }

            sb.Append($"Sec-WebSocket-Key: {GenerateRandomWebSocketKey()}\r\n");

            if (subprotocols != null)
            {
                var subprotocol = $"Sec-WebSocket-Protocol: {subprotocols.Aggregate((current, protocol) => $"{current}, {protocol}")}";

                sb.Append($"{subprotocol}\r\n");
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
