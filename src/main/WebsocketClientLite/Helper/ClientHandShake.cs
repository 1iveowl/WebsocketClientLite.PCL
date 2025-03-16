using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WebsocketClientLite.Helper;

internal static class ClientHandShake
{
    // For .NET Standard 2.0/2.1
    private static readonly object _randomLock = new();
    private static Random? _random;

    internal static byte[] Compose(
    Uri uri,
    string? origin = null,
    IDictionary<string, string>? headers = null,
    IEnumerable<string>? subprotocols = null)
    {
        // Pre-allocate StringBuilder with a reasonable initial capacity
        var sb = new StringBuilder(512);

        // Use direct Append chains instead of string interpolation
        sb.Append("GET ").Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
        sb.Append("Host: ").Append(uri.Host).Append("\r\n");
        sb.Append("Upgrade: websocket\r\n");
        sb.Append("Connection: Upgrade\r\n");

        // Add headers if present
        if (headers is not null)
        {
            foreach (var header in headers)
            {
                sb.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }
        }

        // Add origin if provided
        if (!string.IsNullOrEmpty(origin))
        {
            sb.Append("Origin: ").Append(origin).Append("\r\n");
        }

        // Generate websocket key
        string key = GenerateRandomWebSocketKey();
        sb.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n");

        // Handle subprotocols more efficiently
        if (subprotocols is not null && subprotocols.Any())
        {
            sb.Append("Sec-WebSocket-Protocol: ");

            // Avoid LINQ Aggregate which creates multiple string objects
            bool isFirst = true;
            foreach (var protocol in subprotocols)
            {
                if (!isFirst)
                    sb.Append(", ");
                sb.Append(protocol);
                isFirst = false;
            }
            sb.Append("\r\n");
        }

        sb.Append("Sec-WebSocket-Version: 13\r\n\r\n");

        // Get the result as a byte array
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    static string GenerateRandomWebSocketKey()
    {
        var webSocketKey = new byte[16];

        // Use Random.Shared for .NET 6+ or create a static Random instance for earlier versions
#if NET6_0_OR_GREATER
    Random.Shared.NextBytes(webSocketKey);
#else
        // Use a static Random instance for thread safety in older frameworks
        lock (_randomLock)
        {
            _random ??= new Random();
            _random.NextBytes(webSocketKey);
        }
#endif
        return Convert.ToBase64String(webSocketKey);
    }

}
