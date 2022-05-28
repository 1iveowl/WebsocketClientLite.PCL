using System;
using System.Collections.Generic;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Helper
{
    internal static class WebsocketMasking
    {
        private const byte FINbit = 0x80;

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
                    firstPayloadByte = (byte)(length + FINbit);
                }
                return new byte[1] { firstPayloadByte};
            }

            if (length >= (byte)PayloadBitLengthKind.Bits16 && length <= Math.Pow(2, 16))
            {
                if (isMasking)
                {
                    firstPayloadByte = (byte)PayloadBitLengthKind.Bits16 + FINbit;
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

            if (length >= Math.Pow(2, 16) && length <= Math.Pow(2, 64))
            {
                if (isMasking)
                {
                    firstPayloadByte = (byte)PayloadBitLengthKind.Bits64 + FINbit;
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
}
