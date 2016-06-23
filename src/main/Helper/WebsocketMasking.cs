using System;

namespace WebsocketClientLite.PCL.Helper
{
    internal static class WebsocketMasking
    {
        internal static byte[] Encode(byte[] data, byte[] key)
        {
            return EncodeDecodeSymmetric(data, key);
        }

        internal static byte[] Decode(byte[] data, byte[] key)
        {
            return EncodeDecodeSymmetric(data, key);
        }

        private static byte[] EncodeDecodeSymmetric(byte[] data, byte[] key)
        {
            var result = new byte[data.Length];

            for (var i = 0; i < data.Length; i++)
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
            

            if (length <= 125)
            {
                if (isMasking)
                {
                    length = length + 128;
                }
                return new byte[1] {(byte)length};
            }

            if (length > 125 && length <= Math.Pow(2, 16))
            {
                if (isMasking)
                {
                    length = length + 128;
                }
                return BitConverter.GetBytes((short)length);
            }

            if (length > Math.Pow(2, 64))
            {
                throw new ArgumentException("Too long message for one frame");
            }

            if (isMasking)
            {
                length = length + 128;
            }

            return BitConverter.GetBytes((long)length);

        }
    }
}
