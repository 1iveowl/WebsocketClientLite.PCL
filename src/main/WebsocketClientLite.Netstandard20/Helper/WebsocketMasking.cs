using System;
using System.Collections.Generic;
using WebsocketClientLite.PCL.CustomException;

namespace WebsocketClientLite.PCL.Helper
{
    internal static class WebsocketMasking
    {
        internal static byte[] Encode(byte[] data, byte[] key)
        {
            return SymmetricCoding(data, key);
        }

        internal static byte[] Decode(byte[] data, byte[] key)
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

            if (length < 126)
            {
                if (isMasking)
                {
                    firstPayloadByte = (byte)(length + 128);
                }
                return new byte[1] { firstPayloadByte};
            }

            if (length >= 126 && length <= Math.Pow(2, 16))
            {
                if (isMasking)
                {
                    firstPayloadByte = 126 + 128;
                }

                var payloadLength = BitConverter.GetBytes((short)length);

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
                    firstPayloadByte = 127 + 128;
                }

                var payloadLength = BitConverter.GetBytes((long)length);

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
