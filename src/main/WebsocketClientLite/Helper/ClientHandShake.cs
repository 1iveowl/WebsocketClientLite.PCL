using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WebsocketClientLite.Helper;

internal static class ClientHandShake
{

#if NETSTANDARD2_0
    // For .NET Standard 2.0/2.1
    private static readonly object _randomLock = new();
    private static Random? _random;
#endif

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
        if (subprotocols?.Count() > 0)
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
#if NETSTANDARD2_0
        // netstandard2.0 doesn't have RandomNumberGenerator.Fill nor Convert.ToBase64String(Span<byte>)
        var webSocketKey = new byte[16];

        lock (_randomLock)
        {
            using var rng = RandomNumberGenerator.Create();
            _random ??= new Random();
            _random.NextBytes(webSocketKey);
            rng.GetBytes(webSocketKey);
        }

#else
        Span<byte> webSocketKey = stackalloc byte[16];
        RandomNumberGenerator.Fill(webSocketKey);

#endif
        return Convert.ToBase64String(webSocketKey);
    }
}
