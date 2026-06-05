using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;

namespace WebsocketClientLite.Helper;

internal static class DataframeParsing
{
    internal static async Task<Dataframe?> CreateDataframe(this TcpConnectionService tcpConnection, CancellationToken ct)
    {
        var dataframe = new Dataframe(tcpConnection, ct);

        var oneByteArray = await dataframe.GetNextBytes(1).ConfigureAwait(false);

        if (oneByteArray is null)
        {
            return null;
        }

#if DEBUG
        Debug.WriteLine($"First byte: {oneByteArray[0]}");
#endif

        byte firstByte = oneByteArray[0];

        bool fin = (firstByte & 0x80) != 0;    // bit 7
        bool rsv1 = (firstByte & 0x40) != 0;   // bit 6
        bool rsv2 = (firstByte & 0x20) != 0;   // bit 5
        bool rsv3 = (firstByte & 0x10) != 0;   // bit 4

        OpcodeKind opcode = (OpcodeKind)(firstByte & 0x0F); // low 4 bits

#if DEBUG
        Debug.WriteLine($"Opcode: {opcode}");
#endif

        // Classify the frame's role from the FIN bit and opcode (the actual
        // protocol signals). The previous code compared the whole first byte
        // against FragmentKind values, whose numeric values (0x00/0x80) collide
        // with the opcode/FIN bits, so it only ever matched bare continuation
        // frames.
        FragmentKind fragmentKind = (fin, opcode) switch
        {
            (false, not OpcodeKind.Continuation) => FragmentKind.First, // first fragment of a message
            (true, OpcodeKind.Continuation) => FragmentKind.Last,       // final continuation fragment
            _ => FragmentKind.None,                                     // single frame or middle continuation
        };

        return dataframe with
        {
            FIN = fin,
            RSV1 = rsv1,
            RSV2 = rsv2,
            RSV3 = rsv3,
            Opcode = opcode,
            Fragment = fragmentKind
        };
    }

    internal static async Task<Dataframe?> PayloadBitLenght(this Task<Dataframe?> dataframeTask)
    {
        var dataframe = await dataframeTask.ConfigureAwait(false);

        if (dataframe is null)
        {
            return null;
        }

        var oneByteArray = await dataframe.GetNextBytes(1).ConfigureAwait(false);
        if (oneByteArray is null)
        {
            return dataframe;
        }

        byte b = oneByteArray[0];
        bool mask = (b & 0x80) != 0; // bit 7
        byte lenMarker = (byte)(b & 0x7F); // low 7 bits

        return lenMarker switch
        {
            <= (byte)PayloadBitLengthKind.Bits8 => dataframe with
            {
                MASK = mask,
                Length = lenMarker,
                PayloadBitLength = PayloadBitLengthKind.Bits8
            },
            (byte)PayloadBitLengthKind.Bits16 => dataframe with
            {
                MASK = mask,
                PayloadBitLength = PayloadBitLengthKind.Bits16
            },
            _ => dataframe with
            {
                MASK = mask,
                PayloadBitLength = PayloadBitLengthKind.Bits64
            }
        };
    }

    internal static async Task<Dataframe?> PayloadLenght(this Task<Dataframe?> dataframeTask)
    {
        var dataframe = await dataframeTask.ConfigureAwait(false);

        if (dataframe is null)
        {
            return null;
        }

        return dataframe.PayloadBitLength switch
        {
            PayloadBitLengthKind.Bits8 => dataframe,
            PayloadBitLengthKind.Bits16 => await HandleBits16().ConfigureAwait(false),
            PayloadBitLengthKind.Bits64 => await HandleBits64().ConfigureAwait(false),
            _ => throw new WebsocketClientLiteException("Unspecified payload length.")
        };

        async Task<Dataframe?> HandleBits16()
        {
            var bytes = await dataframe.GetNextBytes(2).ConfigureAwait(false);
            if (bytes is null)
            {
                return dataframe;
            }

            // Big-endian manual parse
            ushort len = (ushort)((bytes[0] << 8) | bytes[1]);
            return dataframe with { Length = len };
        }

        async Task<Dataframe?> HandleBits64()
        {
            var bytes = await dataframe.GetNextBytes(8).ConfigureAwait(false);
            if (bytes is null)
            {
                return dataframe;
            }

            // Big-endian manual parse
            ulong len = ((ulong)bytes[0] << 56)
                      | ((ulong)bytes[1] << 48)
                      | ((ulong)bytes[2] << 40)
                      | ((ulong)bytes[3] << 32)
                      | ((ulong)bytes[4] << 24)
                      | ((ulong)bytes[5] << 16)
                      | ((ulong)bytes[6] << 8)
                      | bytes[7];
            return dataframe with { Length = len };
        }
    }

    internal static async Task<Dataframe?> GetPayload(this Task<Dataframe?> dataframeTask, CancellationToken ct = default)
    {
        var dataframe = await dataframeTask.ConfigureAwait(false);

        if (dataframe is null)
        {
            return null;
        }

        // RFC 6455 §5.5: control frames (Close/Ping/Pong) must not be fragmented
        // and must carry a payload of 125 bytes or fewer.
        if (dataframe.Opcode is OpcodeKind.Close or OpcodeKind.Ping or OpcodeKind.Pong)
        {
            if (!dataframe.FIN)
            {
                throw new WebsocketClientLiteException(
                    $"Protocol error: received a fragmented control frame ({dataframe.Opcode}).");
            }

            if (dataframe.Length > 125)
            {
                throw new WebsocketClientLiteException(
                    $"Protocol error: control frame ({dataframe.Opcode}) payload length {dataframe.Length} exceeds 125 bytes.");
            }
        }

        // Guard against memory exhaustion: reject a frame whose declared payload
        // length exceeds the configured maximum before allocating any buffer sized
        // by that (server-controlled) value.
        if (dataframe.Length > (ulong)dataframe.MaxFrameSize)
        {
            throw new WebsocketClientLiteException(
                $"Incoming frame payload length ({dataframe.Length} bytes) exceeds the configured maximum of {dataframe.MaxFrameSize} bytes.");
        }

        // Ensure zero-length frames still consume masking key (if present) and produce empty payload
        if (dataframe.Length == 0)
        {
            if (dataframe.MASK)
            {
                var maskingBytesZero = await dataframe.GetNextBytes(4).ConfigureAwait(false);
                return dataframe with
                {
                    MaskingBytes = maskingBytesZero,
                    DataStream = new MemoryStream() // empty
                };
            }
            else
            {
                return dataframe with { DataStream = new MemoryStream() }; // empty
            }
        }

        var memoryStream = new MemoryStream(checked((int)Math.Min(dataframe.Length, int.MaxValue)));

        if (dataframe.MASK)
        {
            // For masked frames, the masking key comes before the payload per RFC 6455
            var maskingBytes = await dataframe.GetNextBytes(4).ConfigureAwait(false);

            // Read the masked payload bytes
            var nextBytes = await dataframe.GetNextBytes(dataframe.Length).ConfigureAwait(false);
            if (nextBytes is not null)
            {
#if NETSTANDARD2_1
                await memoryStream.WriteAsync(nextBytes, ct).ConfigureAwait(false);
#else
#if NETSTANDARD2_0
                await memoryStream.WriteAsync(nextBytes, 0, nextBytes.Length, ct).ConfigureAwait(false);
#else
                await memoryStream.WriteAsync(nextBytes.AsMemory(), ct).ConfigureAwait(false);
#endif
#endif
            }

            return dataframe with
            {
                MaskingBytes = maskingBytes,
                DataStream = memoryStream
            };
        }
        else
        {
            // Unmasked frames: just read payload
            var nextBytes = await dataframe.GetNextBytes(dataframe.Length).ConfigureAwait(false);
            if (nextBytes is not null)
            {
#if NETSTANDARD2_1
                await memoryStream.WriteAsync(nextBytes, ct).ConfigureAwait(false);
#else
#if NETSTANDARD2_0
                await memoryStream.WriteAsync(nextBytes, 0, nextBytes.Length, ct).ConfigureAwait(false);
#else
                await memoryStream.WriteAsync(nextBytes.AsMemory(), ct).ConfigureAwait(false);
#endif
#endif
            }

            return dataframe with
            {
                DataStream = memoryStream
            };
        }
    }
}
