using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite;
using WebsocketClientLiteTest;

// End-to-end comparison over loopback through the public API, not a kernel benchmark.
// The server echoes each frame with one write. The test helper's EchoLoopAsync writes the
// header and the payload separately, which on loopback makes every small echo wait about
// 40 ms for a delayed ACK (Nagle), so it would measure TCP timers, not the client.
// The in-process server shares the process, so its work and allocations are counted too;
// it is the same code in both builds. Each workload runs one warm-up trial and seven
// measured trials and reports the median, minimum and maximum per operation.
const int Trials = 7;
var library = typeof(ClientWebSocketRx).Assembly;
int stateMachines = library.GetTypes().Count(t => typeof(IAsyncStateMachine).IsAssignableFrom(t));
const MethodImplAttributes AsyncFlag = (MethodImplAttributes)0x2000; // MethodImplOptions.Async
int runtimeAsyncMethods = library.GetTypes()
    .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
    .Count(m => (m.MethodImplementationFlags & AsyncFlag) != 0);

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"Library: {library.Location}");
Console.WriteLine($"Library async state machine types: {stateMachines}; methods marked async to the runtime: {runtimeAsyncMethods}");
Console.WriteLine("workload,operations,median_us,min_us,max_us,alloc_B_per_op");

await Run("connect-handshake-close", 300, Sessions);
await Run("echo-32B", 20000, count => Echo(count, 32));
await Run("echo-4KiB", 10000, count => Echo(count, 4096));
await Run("echo-64KiB", 2000, count => Echo(count, 65536));

static async Task Run(string name, int count, Func<int, Task> workload)
{
    await workload(Math.Max(1, count / 5)).ConfigureAwait(false); // warm-up: tiering and pools
    var perOp = new double[Trials];
    var allocations = new double[Trials];
    for (int trial = 0; trial < Trials; trial++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        await workload(count).ConfigureAwait(false);
        watch.Stop();
        allocations[trial] = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)count;
        perOp[trial] = watch.Elapsed.TotalMicroseconds / count;
    }
    Array.Sort(perOp); Array.Sort(allocations);
    Console.WriteLine($"{name},{count},{perOp[Trials / 2]:F2},{perOp[0]:F2},{perOp[^1]:F2},{allocations[Trials / 2]:F0}");
}

static async Task Sessions(int count)
{
    for (int i = 0; i < count; i++)
    {
        using var server = new LoopbackWebSocketServer();
        server.Start(EchoOneWriteAsync);
        await using var client = new Client(server.Uri);
        await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
    }
}

static async Task Echo(int count, int size)
{
    using var server = new LoopbackWebSocketServer();
    server.Start(EchoOneWriteAsync);
    await using var client = new Client(server.Uri);
    await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
    var message = new string('x', size);
    for (int i = 0; i < count; i++)
    {
        await client.Socket.Sender!.SendText(message).ConfigureAwait(false);
        var echoed = await client.Messages.Reader.ReadAsync().ConfigureAwait(false);
        if (echoed.Length != size) throw new InvalidOperationException("Echo returned the wrong message.");
    }
}

// Echoes data frames and mirrors Close, writing each server frame (unmasked) in one buffer.
static async Task EchoOneWriteAsync(System.IO.Stream stream, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var frame = await LoopbackWebSocketServer.ReadFrameAsync(stream, ct).ConfigureAwait(false);
        if (frame is null) return;
        var (opcode, fin, payload) = frame.Value;
        if (opcode == 0xA) continue;
        int reply = opcode == 0x9 ? 0xA : opcode;
        int len = payload.Length, head = len <= 125 ? 2 : len <= ushort.MaxValue ? 4 : 10;
        var buffer = new byte[head + len];
        buffer[0] = (byte)((fin ? 0x80 : 0) | reply);
        if (len <= 125) buffer[1] = (byte)len;
        else if (len <= ushort.MaxValue) { buffer[1] = 126; buffer[2] = (byte)(len >> 8); buffer[3] = (byte)len; }
        else { buffer[1] = 127; for (int i = 0; i < 8; i++) buffer[2 + i] = (byte)((long)len >> (8 * (7 - i))); }
        payload.CopyTo(buffer, head);
        await stream.WriteAsync(buffer, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
        if (opcode == 0x8) return;
    }
}

sealed class Client : IAsyncDisposable
{
    private readonly IDisposable _subscription;

    public Client(Uri uri)
    {
        _subscription = Socket.WebsocketConnectWithStatusObservable(uri).Subscribe(
            tuple =>
            {
                if (tuple.state == ConnectionStatus.WebsocketConnected) Connected.TrySetResult();
                else if (tuple.state == ConnectionStatus.DataframeReceived && tuple.dataframe?.Message is { } text)
                    Messages.Writer.TryWrite(text);
            },
            ex => Connected.TrySetException(ex));
    }

    public ClientWebSocketRx Socket { get; } = new();

    public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Channel<string> Messages { get; } = Channel.CreateUnbounded<string>(new() { SingleReader = true });

    public async ValueTask DisposeAsync()
    {
        // DisposeAsync awaits the close handshake (README: `await using` is the preferred lifecycle).
        await Socket.DisposeAsync().ConfigureAwait(false);
        _subscription.Dispose();
    }
}
