using System;
using System.Collections.Generic;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;

namespace WebsocketClientLite.Helper;

internal static class WebsocketMasking
{
#pragma warning disable IDE1006 // Naming Styles
    private const byte MaskBit = 0x80;
#pragma warning restore IDE1006 // Naming Styles

    internal static byte[] Encode(IReadOnlyList<byte> data, IReadOnlyList<byte> key)
    {
        return SymmetricCoding(data, key);
    }

    internal static byte[] Decode(IReadOnlyList<byte> data, IReadOnlyList<byte> key)
    {
        return SymmetricCoding(data, key);
    }

    private static byte[] SymmetricCoding(IReadOnlyList<byte> data, IReadOnlyList<byte> key)
    {
        var result = new byte[data.Count];

        for (var i = 0; i < data.Count; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % 4]);
        }
        return result;
    }

    internal static byte[] CreateMaskKey()
    {
        var rnd = new Random();
        var key = new byte[4];
        rnd.NextBytes(key);
        return key;
    }

    internal static byte[] CreatePayloadBytes(int length, bool isMasking)
    {
        byte firstPayloadByte = 0;

        if (length <= (byte)PayloadBitLengthKind.Bits8)
        {
            if (isMasking)
            {
                firstPayloadByte = (byte)(length + MaskBit);
            }
            return [firstPayloadByte];
        }

        if (length >= (byte)PayloadBitLengthKind.Bits16 && length <= ushort.MaxValue)
        {
            if (isMasking)
            {
                firstPayloadByte = (byte)PayloadBitLengthKind.Bits16 + MaskBit;
            }

            var payloadLength = BitConverter.GetBytes((ushort)length);

            var byteArray = new byte[3]
            {
                firstPayloadByte,
                payloadLength[1],
                payloadLength[0],
                
            };

            return byteArray;
        }

        if (length > ushort.MaxValue && length <= int.MaxValue)
        {
            if (isMasking)
            {
                firstPayloadByte = (byte)PayloadBitLengthKind.Bits64 + MaskBit;
            }

            var payloadLength = BitConverter.GetBytes((ulong)length);

            var byteArray = new byte[9]
            {
                firstPayloadByte,
                payloadLength[7],
                payloadLength[6],
                payloadLength[5],
                payloadLength[4],
                payloadLength[3],
                payloadLength[2],
                payloadLength[1],
                payloadLength[0],
            };

            return byteArray;
        }

        throw new WebsocketClientLiteException("Unable to send message", new ArgumentException("Message too long for one frame"));
    }
}
