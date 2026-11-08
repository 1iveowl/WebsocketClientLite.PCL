using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using WebsocketClientLite.Service;
using WebsocketClientLite.Model;

// Manual experiment only: deliberately absent from the solution and CI.
var watch = Stopwatch.StartNew();
var orders = Permute("DCPRHXA").ToArray();
var legal = orders.Where(s => Before(s, 'D', 'C') && Before(s, 'C', 'P') &&
    Before(s, 'P', 'H')).ToArray();
if (args.Length > 0)
{
    if (args.Length != 1 || !legal.Contains(args[0])) throw new ArgumentException("Expected one legal replay order");
    legal = [args[0]];
}
Console.WriteLine(JsonSerializer.Serialize(new { runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    candidates = orders.Length, legal = legal.Length, discarded = orders.Length - orders.Count(s =>
        Before(s, 'D', 'C') && Before(s, 'C', 'P') && Before(s, 'P', 'H')) }));
foreach (var order in legal)
{
    if (watch.Elapsed > TimeSpan.FromMinutes(5)) throw new TimeoutException("Exploration budget exhausted; incomplete");
    await Run(order);
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = legal.Length, seconds = watch.Elapsed.TotalSeconds,
    peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 }));

static bool Before(string s, char a, char b) => s.IndexOf(a) < s.IndexOf(b);
static IEnumerable<string> Permute(string s) => s.Length == 0 ? [""] :
    s.SelectMany((c, i) => Permute(s.Remove(i, 1)).Select(t => c + t));
static async Task Run(string order)
{
    var trace = new ConcurrentQueue<string>();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var cancel = new CancellationTokenSource();
    using var stream = new MemoryStream();
    using var tcp = new TcpConnectionService(() => false, (_, _, _, _) => true,
        (_, _) => Task.CompletedTask, (_, _) => { }, true, new TcpClient());
    typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tcp, stream);
    int active = 0, overlap = 0, closeWrites = 0, terminal = 0, lateData = 0;
    var ops = new ConcurrentQueue<int>();
    var sender = new WebsocketSenderHandler(tcp, (_, _) => { }, async (_, bytes, _, _) =>
    {
        if (Interlocked.Increment(ref active) != 1) Interlocked.Exchange(ref overlap, 1);
        int op = bytes[0] & 15;
        if (op == 8) Interlocked.Increment(ref closeWrites);
        if ((op == 0 || op == 1) && Volatile.Read(ref terminal) == 1) Interlocked.Exchange(ref lateData, 1);
        ops.Enqueue(op); trace.Enqueue($"write:{op}:enter"); entered.TrySetResult();
        try { await release.Task; }
        finally { trace.Enqueue($"write:{op}:exit"); Interlocked.Decrement(ref active); }
    }, false);
    var tasks = new List<Task>();
    async Task Observe(Task task, string name)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); trace.Enqueue(name + ":ok"); }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or WebsocketClientLite.CustomException.WebsocketClientLiteException)
        { trace.Enqueue(name + ":" + e.GetType().Name); }
    }
    try
    {
        trace.Enqueue("F"); tasks.Add(Observe(sender.SendText(new[] { "first", "last" }), "fragmented"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        foreach (char e in order)
        {
            trace.Enqueue(e.ToString());
            switch (e)
            {
                case 'D': tasks.Add(Observe(sender.SendText("single", cancel.Token), "single")); break;
                case 'C': cancel.Cancel(); await tasks[1]; break;
                case 'P': tasks.Add(Observe(sender.SendPing("control"), "ping")); break;
                case 'R': release.TrySetResult(); await tasks[0]; break;
                case 'H': tasks.Add(Observe(sender.SendCloseHandshakeAsync((StatusCodes)1000), "close")); break;
                case 'X': sender.NotifyPeerClose(); Volatile.Write(ref terminal, 1); break;
                case 'A': tcp.Dispose(); break;
            }
        }
        await Task.WhenAll(tasks);
        if (overlap != 0) throw new InvalidOperationException("Overlapping frame delegates");
        if (lateData != 0) throw new InvalidOperationException("Data admitted after peer Close");
        if (closeWrites > 1) throw new InvalidOperationException("Duplicate Close write");
        if (order == "RDCPHXA" && !ops.SequenceEqual(new[] { 1, 0, 1, 9, 8 }))
            throw new InvalidOperationException("Successful path did not emit both fragments, single data, Ping and Close");
        Console.WriteLine(JsonSerializer.Serialize(new { order, result = "pass", trace = trace.ToArray() }));
    }
    catch (Exception e)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { order, result = "fail", error = e.Message, trace = trace.ToArray() }));
        throw;
    }
    finally
    {
        release.TrySetResult(); cancel.Cancel();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));
    }
}
