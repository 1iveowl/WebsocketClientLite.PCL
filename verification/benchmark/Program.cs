using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using WebsocketClientLite.Helper;

// Comparative microbenchmark, not a network throughput benchmark. Both delegates
// receive the same preallocated arrays; timings include the production adapter.
byte[] key = { 0x37, 0xfa, 0x21, 0x3d };
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine("bytes,iterations,original_ns,verified_ns,ratio,original_alloc_B,verified_alloc_B");
foreach (int size in new[] { 0, 4, 125, 1024, 65536, 1048576 })
{
    var data = new byte[size];
    int iterations = Math.Max(100, Math.Min(2000000, 128 * 1024 * 1024 / Math.Max(size, 1)));
    for (int i = 0; i < Math.Max(10, Math.Min(20000, 16777216 / Math.Max(size, 1))); i++) { Original(data, key); WebsocketMasking.Decode(data, key); }
    var original = new double[7];
    var verified = new double[7];
    long originalBytes = 0, verifiedBytes = 0;
    for (int trial = 0; trial < 7; trial++)
    {
        if (trial % 2 == 0)
        {
            (original[trial], originalBytes) = Measure(Original, data, key, iterations);
            (verified[trial], verifiedBytes) = Measure(WebsocketMasking.Decode, data, key, iterations);
        }
        else
        {
            (verified[trial], verifiedBytes) = Measure(WebsocketMasking.Decode, data, key, iterations);
            (original[trial], originalBytes) = Measure(Original, data, key, iterations);
        }
    }
    Array.Sort(original); Array.Sort(verified);
    Console.WriteLine($"{size},{iterations},{original[3]:F2},{verified[3]:F2},{verified[3]/original[3]:F3},{originalBytes},{verifiedBytes}");
}

Console.WriteLine("copy_bytes,original_ns,verified_ns,ratio,original_alloc_B,verified_alloc_B");
foreach (int size in new[] { 125, 1024, 65536 })
{
    var source = new byte[size];
    var dest = new byte[size + 14];
    Array.Copy(key, dest, 4);
    Func<byte[], byte[], byte[]> originalCopy = (src, unused) => CopyOriginal(src, dest, 14, src.Length, 0);
    Func<byte[], byte[], byte[]> verifiedCopy = (src, unused) => {
        WebsocketClientLite.VerifiedProtocol.Protocol.CopyMasked(src, dest, 14, (uint)src.Length, dest[0], dest[1], dest[2], dest[3]);
        return dest;
    };
    int iterations = 128 * 1024 * 1024 / size;
    var a = new double[7]; var b = new double[7];
    long ab = 0, bb = 0;
    for (int i = 0; i < 1000; i++) { originalCopy(source, key); verifiedCopy(source, key); }
    for (int trial = 0; trial < 7; trial++) {
        if (trial % 2 == 0) {
            (a[trial], ab) = Measure(originalCopy, source, key, iterations);
            (b[trial], bb) = Measure(verifiedCopy, source, key, iterations);
        } else {
            (b[trial], bb) = Measure(verifiedCopy, source, key, iterations);
            (a[trial], ab) = Measure(originalCopy, source, key, iterations);
        }
    }
    Array.Sort(a); Array.Sort(b);
    Console.WriteLine($"{size},{a[3]:F2},{b[3]:F2},{b[3]/a[3]:F3},{ab},{bb}");
}

Console.WriteLine("slice_bytes,original_ns,verified_ns,ratio,original_alloc_B,verified_alloc_B");
foreach (int size in new[] { 125, 1024, 65536 })
{
    var buffer = new byte[size + 14];
    Array.Copy(key, buffer, 4);
    Func<byte[], byte[], byte[]> originalSlice = (data, unused) => SliceOriginal(data, 14, data.Length - 14, 0);
    Func<byte[], byte[], byte[]> verifiedSlice = (data, unused) => {
        WebsocketClientLite.VerifiedProtocol.Protocol.MaskSlice(data, 14, (uint)(data.Length - 14), 0);
        return data;
    };
    int iterations = 128 * 1024 * 1024 / size;
    var a = new double[7]; var b = new double[7];
    long ab = 0, bb = 0;
    for (int i = 0; i < 1000; i++) { originalSlice(buffer, key); verifiedSlice(buffer, key); }
    for (int trial = 0; trial < 7; trial++) {
        if (trial % 2 == 0) {
            (a[trial], ab) = Measure(originalSlice, buffer, key, iterations);
            (b[trial], bb) = Measure(verifiedSlice, buffer, key, iterations);
        } else {
            (b[trial], bb) = Measure(verifiedSlice, buffer, key, iterations);
            (a[trial], ab) = Measure(originalSlice, buffer, key, iterations);
        }
    }
    Array.Sort(a); Array.Sort(b);
    Console.WriteLine($"{size},{a[3]:F2},{b[3]:F2},{b[3]/a[3]:F3},{ab},{bb}");
}

[MethodImpl(MethodImplOptions.NoInlining)]
static byte[] SliceOriginal(byte[] data, int offset, int count, int keyOffset)
{
    for (int i = 0; i < count; i++) data[offset + i] ^= data[keyOffset + (i & 3)];
    return data;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static byte[] CopyOriginal(byte[] source, byte[] dest, int offset, int count, int keyOffset)
{
    for (int i = 0; i < count; i++) dest[offset + i] = (byte)(source[i] ^ dest[keyOffset + (i & 3)]);
    return dest;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static byte[] Original(byte[] data, byte[] key)
{
    for (int i = 0; i < data.Length; i++) data[i] = (byte)(data[i] ^ key[i & 3]);
    return data;
}

static (double, long) Measure(Func<byte[], byte[], byte[]> operation, byte[] data, byte[] key, int iterations)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < iterations; i++) operation(data, key);
    long elapsed = Stopwatch.GetTimestamp() - start;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    GC.KeepAlive(data);
    return (elapsed * (1e9 / Stopwatch.Frequency) / iterations, allocated);
}
