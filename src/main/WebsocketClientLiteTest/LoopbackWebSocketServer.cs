using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebsocketClientLiteTest;

/// <summary>
/// A minimal in-process RFC 6455 server on localhost for end-to-end tests. It
/// performs the server-side handshake and then runs a supplied behavior over the
/// raw stream. No external network is used, so it is safe for CI.
/// </summary>
internal sealed class LoopbackWebSocketServer : IDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverLoop;

    public LoopbackWebSocketServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public Uri Uri => new($"ws://127.0.0.1:{Port}");

    /// <summary>Accept one client, complete the handshake, then run <paramref name="afterHandshake"/>.</summary>
    public void Start(Func<Stream, CancellationToken, Task> afterHandshake)
    {
        _serverLoop = Task.Run(async () =>
        {
            try
            {
                using var tcp = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                using var stream = tcp.GetStream();
                await PerformHandshakeAsync(stream, _cts.Token).ConfigureAwait(false);
                await afterHandshake(stream, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (IOException) { /* client disconnected */ }
            catch (ObjectDisposedException) { /* listener stopped */ }
        });
    }

    /// <summary>
    /// Accept one client, read its handshake request, and reply with the given
    /// raw HTTP response (e.g. a 404) instead of completing the upgrade. The
    /// connection is then held open so the client's behavior is driven purely by
    /// the response content rather than an abrupt EOF.
    /// </summary>
    public void StartWithRawHandshakeResponse(string rawHttpResponse)
    {
        _serverLoop = Task.Run(async () =>
        {
            try
            {
                using var tcp = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                using var stream = tcp.GetStream();
                await ReadRequestHeadersAsync(stream, _cts.Token).ConfigureAwait(false);

                var bytes = Encoding.ASCII.GetBytes(rawHttpResponse);
                await stream.WriteAsync(bytes.AsMemory(), _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);

                await Task.Delay(Timeout.Infinite, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (IOException) { /* client disconnected */ }
            catch (ObjectDisposedException) { /* listener stopped */ }
        });
    }

    /// <summary>Echoes data frames, replies to pings with pongs, and mirrors close.</summary>
    public static async Task EchoLoopAsync(Stream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
            if (frame is null)
            {
                break; // client closed the connection
            }

            var (opcode, fin, payload) = frame.Value;
            switch (opcode)
            {
                case 0x8: // Close
                    await WriteFrameAsync(stream, 0x8, payload, true, ct).ConfigureAwait(false);
                    return;
                case 0x9: // Ping -> Pong
                    await WriteFrameAsync(stream, 0xA, payload, true, ct).ConfigureAwait(false);
                    break;
                case 0xA: // Pong (from client ping) -> ignore
                    break;
                default: // Text/Binary/Continuation -> echo verbatim
                    await WriteFrameAsync(stream, opcode, payload, fin, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>Reads a single frame from the client (client frames are masked) and unmasks it.</summary>
    public static async Task<(int opcode, bool fin, byte[] payload)?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = await ReadExactlyAsync(stream, 2, ct).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        bool fin = (header[0] & 0x80) != 0;
        int opcode = header[0] & 0x0F;
        bool masked = (header[1] & 0x80) != 0;
        long length = header[1] & 0x7F;

        if (length == 126)
        {
            var ext = await ReadExactlyAsync(stream, 2, ct).ConfigureAwait(false) ?? throw new IOException("eof");
            length = (ext[0] << 8) | ext[1];
        }
        else if (length == 127)
        {
            var ext = await ReadExactlyAsync(stream, 8, ct).ConfigureAwait(false) ?? throw new IOException("eof");
            length = 0;
            for (int i = 0; i < 8; i++)
            {
                length = (length << 8) | ext[i];
            }
        }

        byte[]? mask = masked ? await ReadExactlyAsync(stream, 4, ct).ConfigureAwait(false) : null;
        var payload = length == 0 ? Array.Empty<byte>() : await ReadExactlyAsync(stream, (int)length, ct).ConfigureAwait(false) ?? throw new IOException("eof");

        if (masked && mask is not null)
        {
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(payload[i] ^ mask[i & 3]);
            }
        }

        return (opcode, fin, payload);
    }

    /// <summary>Writes an unmasked server frame (server -> client frames are never masked).</summary>
    public static async Task WriteFrameAsync(Stream stream, int opcode, byte[] payload, bool fin, CancellationToken ct)
    {
        int len = payload.Length;
        var header = new byte[len <= 125 ? 2 : len <= ushort.MaxValue ? 4 : 10];
        header[0] = (byte)((fin ? 0x80 : 0x00) | opcode);

        if (len <= 125)
        {
            header[1] = (byte)len;
        }
        else if (len <= ushort.MaxValue)
        {
            header[1] = 126;
            header[2] = (byte)(len >> 8);
            header[3] = (byte)(len & 0xFF);
        }
        else
        {
            header[1] = 127;
            long l = len;
            for (int i = 0; i < 8; i++)
            {
                header[2 + i] = (byte)(l >> (8 * (7 - i)));
            }
        }

        await stream.WriteAsync(header.AsMemory(), ct).ConfigureAwait(false);
        if (len > 0)
        {
            await stream.WriteAsync(payload.AsMemory(), ct).ConfigureAwait(false);
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadExactlyAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null; // EOF
            }
            total += read;
        }
        return buffer;
    }

    private static async Task<string> ReadRequestHeadersAsync(Stream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (!sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            int read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("EOF during handshake");
            }
            sb.Append((char)one[0]);
        }

        return sb.ToString();
    }

    private static async Task PerformHandshakeAsync(Stream stream, CancellationToken ct)
    {
        var request = await ReadRequestHeadersAsync(stream, ct).ConfigureAwait(false);

        var key = TryExtractHeader(request, "Sec-WebSocket-Key")
            ?? throw new IOException("Missing header: Sec-WebSocket-Key");
        var accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));

        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n";

        // Echo the first offered subprotocol, like a real server selecting one.
        var offered = TryExtractHeader(request, "Sec-WebSocket-Protocol");
        if (offered is not null)
        {
            var first = offered.Split(',')[0].Trim();
            response += $"Sec-WebSocket-Protocol: {first}\r\n";
        }

        response += "\r\n";

        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string? TryExtractHeader(string request, string name)
    {
        foreach (var line in request.Split("\r\n"))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring(colon + 1).Trim();
            }
        }
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try { _serverLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore teardown races */ }
        _cts.Dispose();
    }
}
