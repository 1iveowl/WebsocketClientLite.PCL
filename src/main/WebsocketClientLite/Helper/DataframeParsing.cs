using System;
using System.Collections;
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

        var oneByteArray = await dataframe.GetNextBytes(1);

        if (oneByteArray is null)
        {
            return null;
        }

#if DEBUG
        Debug.WriteLine($"First byte: {oneByteArray[0]}");
#endif

        // Extract byte value once to avoid repeated array access
        byte firstByte = oneByteArray[0];

        // Optimize bit operations by extracting directly from the byte value
        // This eliminates the need for BitArray allocation and manipulation
        bool fin = (firstByte & 0x80) != 0;    // 10000000 - Check if bit 7 is set
        bool rsv1 = (firstByte & 0x40) != 0;   // 01000000 - Check if bit 6 is set
        bool rsv2 = (firstByte & 0x20) != 0;   // 00100000 - Check if bit 5 is set
        bool rsv3 = (firstByte & 0x10) != 0;   // 00010000 - Check if bit 4 is set

        // Extract opcode directly - bottom 4 bits https://datatracker.ietf.org/doc/html/rfc6455#section-5.2

        OpcodeKind opcode = (OpcodeKind)(firstByte & 0x0F);

#if DEBUG
        Debug.WriteLine($"Opcode: {opcode}");
#endif

        // Determine fragment kind directly using byte comparison
        FragmentKind fragmentKind = firstByte switch
        {
            (byte)FragmentKind.First => FragmentKind.First,
            (byte)FragmentKind.Last => FragmentKind.Last,
            _ => FragmentKind.None
        };

        // Create the dataframe with all properties set at once
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
        var dataframe = await dataframeTask;

        if (dataframe is null)
        {
            return null;
        }

        var oneByteArray = await dataframe.GetNextBytes(1);

        if (oneByteArray is null)
        {
            return dataframe;
        }

        var bits = new BitArray(oneByteArray);
        var @byte = oneByteArray[0];
        var mask = bits[7];

        // Use switch expression for cleaner, more efficient code
        return @byte switch
        {
            // If byte <= 0x7D (125), it's a 8-bit payload length
            <= (byte)PayloadBitLengthKind.Bits8 => dataframe with
            {
                MASK = mask,
                Length = @byte,
                PayloadBitLength = PayloadBitLengthKind.Bits8
            },

            // If byte == 0x7E (126), it's a 16-bit payload length
            (byte)PayloadBitLengthKind.Bits16 => dataframe with
            {
                MASK = mask,
                PayloadBitLength = PayloadBitLengthKind.Bits16
            },

            // If byte == 0x7F (127), it's a 64-bit payload length
            (byte)PayloadBitLengthKind.Bits64 or > (byte)PayloadBitLengthKind.Bits64 => dataframe with
            {
                MASK = mask,
                PayloadBitLength = PayloadBitLengthKind.Bits64
            }
        };
    }


    internal static async Task<Dataframe?> PayloadLenght(this Task<Dataframe?> dataframeTask)
    {
        var dataframe = await dataframeTask;

        if (dataframe is null)
        {
            return null;
        }

        return dataframe.PayloadBitLength switch
        {
            PayloadBitLengthKind.Bits8 => dataframe,
            PayloadBitLengthKind.Bits16 => await HandleBits16(),
            PayloadBitLengthKind.Bits64 => await HandleBits64(),
            _ => throw new WebsocketClientLiteException("Unspecified payload length.")
        };

        // Local async functions to handle specific cases
        async Task<Dataframe?> HandleBits16()
        {
            var bytes = await dataframe.GetNextBytes(2);
            if (bytes is null)
            {
                return dataframe;
            }

            // Avoid unnecessary array allocation if possible
            if (BitConverter.IsLittleEndian)
            {
                // We need to reverse bytes for little-endian platforms
                Array.Reverse(bytes);
                return dataframe with { Length = BitConverter.ToUInt16(bytes, 0) };
            }
            else
            {
                // For big-endian platforms, no need to reverse
                return dataframe with { Length = BitConverter.ToUInt16(bytes, 0) };
            }
        }

        async Task<Dataframe?> HandleBits64()
        {
            var bytes = await dataframe.GetNextBytes(8);
            if (bytes is null)
            {
                return dataframe;
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
                return dataframe with { Length = BitConverter.ToUInt64(bytes, 0) };
            }
            else
            {
                return dataframe with { Length = BitConverter.ToUInt64(bytes, 0) };
            }
        }
    }

    internal static async Task<Dataframe?> GetPayload(this Task<Dataframe?> dataframeTask)
    {
        var dataframe = await dataframeTask;

        if (dataframe is null || dataframe.Length == 0)
        {
            return dataframe;
        }

        // Only create memory stream when needed
        var memoryStream = new MemoryStream(checked((int)Math.Min(dataframe.Length, int.MaxValue)));

        var nextBytes = await dataframe.GetNextBytes(dataframe.Length);
        if (nextBytes is not null)
        {
#if NETSTANDARD2_1
            await memoryStream.WriteAsync(nextBytes);
#else
            await memoryStream.WriteAsync(nextBytes, 0, nextBytes.Length);
#endif
        }

        if (!dataframe.MASK)
        {
            return dataframe with
            {
                DataStream = memoryStream
            };
        }

        // Only get masking bytes when needed
        var maskingBytes = await dataframe.GetNextBytes(4);
        if (maskingBytes is not null)
        {
            Array.Reverse(maskingBytes);
        }

        return dataframe with
        {
            MaskingBytes = maskingBytes,
            DataStream = memoryStream
        };
    }
}
