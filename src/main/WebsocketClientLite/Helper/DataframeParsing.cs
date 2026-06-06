using System;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;

namespace WebsocketClientLite.Helper;

internal static class DataframeParsing
{
    /// <summary>
    /// Reads a single WebSocket frame from the connection. The payload is read
    /// once into a byte array and unmasked in place (no intermediate MemoryStream
    /// and no per-step record copies). Returns <see langword="null"/> if the
    /// stream ends before a complete frame is available.
    /// </summary>
    internal static async Task<Dataframe?> ReadDataframeAsync(
        this TcpConnectionService tcpConnection,
        CancellationToken ct)
    {
        var header = await tcpConnection.ReadBytesFromStream(2, ct).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        byte b0 = header[0];
        byte b1 = header[1];

        bool fin = (b0 & 0x80) != 0;
        bool rsv1 = (b0 & 0x40) != 0;
        bool rsv2 = (b0 & 0x20) != 0;
        bool rsv3 = (b0 & 0x10) != 0;
        var opcode = (OpcodeKind)(b0 & 0x0F);

        bool mask = (b1 & 0x80) != 0;
        byte lengthMarker = (byte)(b1 & 0x7F);

        ulong length;
        if (lengthMarker <= 125)
        {
            length = lengthMarker;
        }
        else if (lengthMarker == 126)
        {
            var ext = await tcpConnection.ReadBytesFromStream(2, ct).ConfigureAwait(false);
            if (ext is null)
            {
                return null;
            }
            length = (ulong)((ext[0] << 8) | ext[1]);
        }
        else
        {
            var ext = await tcpConnection.ReadBytesFromStream(8, ct).ConfigureAwait(false);
            if (ext is null)
            {
                return null;
            }
            length = ((ulong)ext[0] << 56)
                   | ((ulong)ext[1] << 48)
                   | ((ulong)ext[2] << 40)
                   | ((ulong)ext[3] << 32)
                   | ((ulong)ext[4] << 24)
                   | ((ulong)ext[5] << 16)
                   | ((ulong)ext[6] << 8)
                   | ext[7];
        }

        // RFC 6455 §5.5: control frames must not be fragmented and must carry a
        // payload of 125 bytes or fewer.
        if (opcode is OpcodeKind.Close or OpcodeKind.Ping or OpcodeKind.Pong)
        {
            if (!fin)
            {
                throw new WebsocketClientLiteException(
                    $"Protocol error: received a fragmented control frame ({opcode}).");
            }

            if (length > 125)
            {
                throw new WebsocketClientLiteException(
                    $"Protocol error: control frame ({opcode}) payload length {length} exceeds 125 bytes.");
            }
        }

        // Guard against memory exhaustion before allocating a payload-sized buffer.
        if (length > (ulong)tcpConnection.MaxFrameSize)
        {
            throw new WebsocketClientLiteException(
                $"Incoming frame payload length ({length} bytes) exceeds the configured maximum of {tcpConnection.MaxFrameSize} bytes.");
        }

        byte[]? maskKey = null;
        if (mask)
        {
            // For masked frames the masking key precedes the payload per RFC 6455.
            maskKey = await tcpConnection.ReadBytesFromStream(4, ct).ConfigureAwait(false);
            if (maskKey is null)
            {
                return null;
            }
        }

        byte[] payload;
        if (length == 0)
        {
            payload = Array.Empty<byte>();
        }
        else
        {
            var read = await tcpConnection.ReadBytesFromStream(length, ct).ConfigureAwait(false);
            if (read is null)
            {
                return null;
            }

            payload = read;

            if (mask)
            {
                WebsocketMasking.Decode(payload, maskKey!); // unmask in place
            }
        }

        var fragment = (fin, opcode) switch
        {
            (false, not OpcodeKind.Continuation) => FragmentKind.First, // first fragment of a message
            (true, OpcodeKind.Continuation) => FragmentKind.Last,       // final continuation fragment
            _ => FragmentKind.None,                                     // single frame or middle continuation
        };

        return new Dataframe
        {
            FIN = fin,
            RSV1 = rsv1,
            RSV2 = rsv2,
            RSV3 = rsv3,
            MASK = mask,
            Opcode = opcode,
            Fragment = fragment,
            Length = length,
            Payload = payload,
        };
    }
}
