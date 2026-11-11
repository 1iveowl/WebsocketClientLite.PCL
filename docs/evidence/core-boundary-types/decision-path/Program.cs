// Evidence-only probe for S8 of the 10.3 session plan, written 2026-11-11.
// It measures the per-frame receive and send paths that the decision calls sit on
// (Admit, Step, SendStep, Outgoing), which verification/benchmark does not run.
// Each scenario: one warm-up trial, then seven trials; the median is reported.
// Allocation is GC.GetTotalAllocatedBytes(precise: true) over the trial, divided
// by the frame count; it includes the per-trial setup, which is the same in every run.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.Service;

internal static class Program
{
    private const int Frames = 20000;
    private const int Trials = 7;

    private static TcpConnectionService Connection(Stream stream)
    {
        var tcp = new TcpConnectionService(() => false, (_, _, _, _) => true, (_, _) => Task.CompletedTask,
            (_, _) => { }, true, new System.Net.Sockets.TcpClient());
        typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tcp, stream);
        return tcp;
    }

    private static byte[] Wire(Func<int, byte[]> frame)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < Frames; i++) bytes.AddRange(frame(i));
        return bytes.ToArray();
    }

    private static byte[] Frame(byte first, int length, byte fill)
    {
        var header = length <= 125 ? new[] { first, (byte)length }
            : new[] { first, (byte)126, (byte)(length >> 8), (byte)length };
        return header.Concat(Enumerable.Repeat(fill, length)).ToArray();
    }

    private static async Task<int> Receive(byte[] wire)
    {
        using var tcp = Connection(new MemoryStream(wire, writable: false));
        using var parser = new WebsocketParserHandler(tcp);
        int count = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = parser.DataframeObservable().Subscribe(
            _ => count++, _ => done.TrySetResult(true), () => done.TrySetResult(true));
        await done.Task.ConfigureAwait(false);
        return count;
    }

    private static async Task<int> Send(bool text, int length)
    {
        using var tcp = Connection(new MemoryStream());
        int writes = 0;
        var sender = new WebsocketSenderHandler(tcp, (_, _) => { },
            (_, _, _, _) => { writes++; return Task.CompletedTask; }, false);
        var binary = Enumerable.Repeat((byte)0x61, length).ToArray();
        var message = new string('a', length);
        for (int i = 0; i < Frames; i++)
        {
            if (text) await sender.SendText(message).ConfigureAwait(false);
            else await sender.SendBinary(binary, CancellationToken.None).ConfigureAwait(false);
        }
        return writes;
    }

    private static async Task Measure(string name, int expected, Func<Task<int>> trial)
    {
        if (await trial().ConfigureAwait(false) != expected) throw new InvalidOperationException(name + ": warm-up count");
        var ns = new List<double>();
        var bytes = new List<double>();
        for (int t = 0; t < Trials; t++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            int count = await trial().ConfigureAwait(false);
            watch.Stop();
            long after = GC.GetTotalAllocatedBytes(true);
            if (count != expected) throw new InvalidOperationException(name + ": count " + count);
            ns.Add(watch.Elapsed.TotalMilliseconds * 1e6 / Frames);
            bytes.Add((after - before) / (double)Frames);
        }
        static double Median(List<double> v) { var s = v.OrderBy(x => x).ToArray(); return s[s.Length / 2]; }
        Console.WriteLine($"{name}\tframes={Frames}\tmedian_ns_per_frame={Median(ns):F1}\tmedian_bytes_per_frame={Median(bytes):F2}\tmin_bytes={bytes.Min():F2}\tmax_bytes={bytes.Max():F2}");
    }

    private static async Task Main()
    {
        Console.WriteLine($"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} tiered={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}");
        var binary16 = Wire(_ => Frame(0x82, 16, 1));
        var text125 = Wire(_ => Frame(0x81, 125, 0x61));
        var binary200 = Wire(_ => Frame(0x82, 200, 2));
        // Frames/2 messages of a first fragment and a final continuation: Frames frames.
        var fragmented = Wire(i => i % 2 == 0 ? Frame(0x02, 8, 3) : Frame(0x80, 8, 4));
        var pingText = Wire(i => i % 2 == 0 ? Frame(0x89, 4, 5) : Frame(0x81, 16, 0x61));
        await Measure("receive binary 16 B", Frames, () => Receive(binary16));
        await Measure("receive text 125 B", Frames, () => Receive(text125));
        await Measure("receive binary 200 B (16-bit length)", Frames, () => Receive(binary200));
        await Measure("receive fragmented 2 x 8 B", Frames / 2, () => Receive(fragmented));
        await Measure("receive ping + text 16 B", Frames, () => Receive(pingText));
        await Measure("send binary 16 B", Frames, () => Send(false, 16));
        await Measure("send text 125 B", Frames, () => Send(true, 125));
    }
}
