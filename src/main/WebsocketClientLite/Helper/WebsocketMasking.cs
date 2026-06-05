using System.Collections.Generic;
using System.Security.Cryptography;

namespace WebsocketClientLite.Helper;

internal static class WebsocketMasking
{
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
        var key = new byte[4];
#if NETSTANDARD2_0
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
#else
        RandomNumberGenerator.Fill(key);
#endif
        return key;
    }
}
