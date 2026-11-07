using System;
using System.Security.Cryptography;

namespace WebsocketClientLite.Helper;

internal static class WebsocketMasking
{
    /// <summary>
    /// XOR-unmasks the payload in place using the 4-byte masking key (RFC 6455
    /// masking is symmetric). Calls the verified Dafny kernel and
    /// allocates nothing; returns the same array for convenience.
    /// </summary>
    internal static byte[] Decode(byte[] data, byte[] key)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);
#else
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (key is null) throw new ArgumentNullException(nameof(key));
#endif
        if (key.Length < 4) throw new ArgumentException("A masking key requires four bytes.", nameof(key));
        if (ReferenceEquals(data, key)) throw new ArgumentException("Payload and key must be separate buffers.", nameof(key));

        // Enforce the generated method's preconditions at the C# boundary.
        Verified.Masking.Apply(data, key, (uint)data.Length);
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
