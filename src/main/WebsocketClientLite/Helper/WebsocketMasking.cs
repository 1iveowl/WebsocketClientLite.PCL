using System.Security.Cryptography;

namespace WebsocketClientLite.Helper;

internal static class WebsocketMasking
{
    /// <summary>
    /// XOR-unmasks the payload in place using the 4-byte masking key (RFC 6455
    /// masking is symmetric). Uses direct array indexing and a bitwise <c>&amp; 3</c>
    /// (instead of an <see cref="IReadOnlyList{T}"/> indexer and <c>% 4</c>) and
    /// allocates nothing; returns the same array for convenience.
    /// </summary>
    internal static byte[] Decode(byte[] data, byte[] key)
    {
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(data[i] ^ key[i & 3]);
        }
        return data;
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
