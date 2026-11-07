using System;
using Protocol = WebsocketClientLite.VerifiedProtocol.Protocol;
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
    /// once into a byte array after validating the server header. Cancellation
    /// returns null; truncated input fails through the connection reader.
    /// </summary>
    internal static async Task<Dataframe?> ReadDataframeAsync(
        this TcpConnectionService tcpConnection,
        CancellationToken ct)
    {
        var header = await tcpConnection.ReadHeaderBytesAsync(2, ct).ConfigureAwait(false);
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
        Protocol.DecodeHeader(b0, b1, Array.Empty<byte>(), 0, (ulong)tcpConnection.MaxFrameSize,
            out byte status, out ulong length, out uint headerSize);
        if (status == 0)
        {
            int count = (int)headerSize - 2;
            var ext = await tcpConnection.ReadHeaderBytesAsync(count, ct).ConfigureAwait(false);
            if (ext is null) return null;
            Protocol.DecodeHeader(b0, b1, ext, (uint)count, (ulong)tcpConnection.MaxFrameSize,
                out status, out length, out _);
        }
        if (status != 2)
            throw new WebsocketClientLiteException("Protocol error: invalid server frame header or payload exceeds the configured maximum.");

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

            payload = read;        }

        if (opcode == OpcodeKind.Close) ProtocolPayload.ValidateClose(payload);

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
