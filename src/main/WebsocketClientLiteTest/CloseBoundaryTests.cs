using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;
using Xunit;
using Xunit.Abstractions;

namespace WebsocketClientLiteTest;

// Real opening handshake, controlled parser source/write/peer timer/periodic scheduler.
// The parser and real socket integration remain covered by ControlledCloseTests/TeardownTests.
public class CloseBoundaryTests(ITestOutputHelper output)
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Harness(ITestOutputHelper output) : IAsyncDisposable
    {
        internal readonly ConcurrentQueue<string> Events = new();
        internal readonly Subject<Dataframe?> Source = new();
        internal readonly TrackingScheduler Scheduler = new();
        internal readonly TaskCompletionSource WriteEntered = Signal(), ResumeWrite = Signal(), PeerWait = Signal(), Expire = Signal();
        internal readonly TaskCompletionSource Completed = Signal(), Error = Signal(), Aborted = Signal(), Disconnected = Signal();
        internal readonly ConcurrentQueue<int> Frames = new();
        internal readonly LoopbackWebSocketServer Server = new();
        internal WebsocketConnectionHandler Connection = null!;
        internal WebsocketSenderHandler Sender = null!;
        internal IDisposable? Subscription;
        internal Exception? StatusError, ObserverError;
        internal bool Established, PauseWrite;
        internal CancellationToken WriteToken;
        internal int TerminalCount;
        private TcpConnectionService _tcp = null!;
        internal Lazy<Task> Close => (Lazy<Task>)typeof(WebsocketConnectionHandler).GetField("_closeHandshakeOnce", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Connection)!;
        internal async Task Start(bool ping = false)
        {
            Server.Start((_, ct) => Task.Delay(Timeout.Infinite, ct));
            _tcp = new TcpConnectionService(() => false, (_, _, _, _) => true,
                (client, uri) => client.ConnectAsync(uri.Host, uri.Port), (_, _) => { }, true);
            Connection = new WebsocketConnectionHandler(_tcp, new WebsocketParserHandler(_tcp), (status, error) =>
            {
                Events.Enqueue(status.ToString());
                if (status == ConnectionStatus.Aborted) { StatusError = error; Aborted.TrySetResult(); }
                if (status == ConnectionStatus.Disconnected) Disconnected.TrySetResult();
            }, (_, callback) => Sender = new WebsocketSenderHandler(_tcp, callback, async (stream, bytes, count, ct) =>
            {
                if (!Established) { await stream.WriteAsync(bytes.AsMemory(0, count), ct); return; }
                WriteToken = ct;
                Frames.Enqueue(bytes[0] & 15); Events.Enqueue("write-entered"); WriteEntered.TrySetResult();
                if (PauseWrite) await ResumeWrite.Task.WaitAsync(ct);
                Events.Enqueue("write-completed");
            }, false, (_, _) => { Events.Enqueue("peer-wait"); PeerWait.TrySetResult(); return Expire.Task; }), Scheduler);
            await Connection.ConnectWebsocket(Server.Uri, null, System.Security.Authentication.SslProtocols.None,
                (_, _) => { }, ping, TimeSpan.FromSeconds(1), "ping", TimeSpan.FromSeconds(10), null, null, null, CancellationToken.None);
            Established = true;
            var observable = Observable.Create<IDataframe?>((observer, token) =>
                Connection.ObserveFrames(Source, observer, Sender, () => Close.Value, token));
            Subscription = observable.Subscribe(_ => Events.Enqueue("data"), ex =>
            {
                Events.Enqueue("observer-error"); ObserverError = ex; Interlocked.Increment(ref TerminalCount); Error.TrySetResult();
            }, () => { Events.Enqueue("observer-completed"); Interlocked.Increment(ref TerminalCount); Completed.TrySetResult(); });
        }
        internal async Task Wait(Task task)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException e) { throw new TimeoutException("Boundary schedule: " + string.Join(" -> ", Events), e); }
        }
        public async ValueTask DisposeAsync()
        {
            // Release every injected hold before awaiting cleanup, including assertion failures.
            ResumeWrite.TrySetResult(); Expire.TrySetResult();
            if (Sender is not null) Sender.NotifyReadFailure();
            Subscription?.Dispose();
            try { if (Connection is not null) await Wait(Connection.DisposeAsync().AsTask()); }
            finally { _tcp?.Dispose(); Source.Dispose(); Server.Dispose(); output.WriteLine(string.Join(" -> ", Events)); }
        }
    }

    [Fact]
    public async Task ReadFailureReleasesReadinessBeforeObserverError()
    {
        await using var h = new Harness(output); await h.Start();
        // Check the state at a causal checkpoint, not after a timer or cleanup can release it.
        var failure = new IOException("controlled parser failure");
        var observer = System.Reactive.Observer.Create<IDataframe?>(_ => { }, ex =>
        {
            h.Events.Enqueue("observer-error"); h.ObserverError = ex;
        });
        await h.Wait(h.Connection.ObserveFrames(Observable.Throw<Dataframe?>(failure), observer,
            h.Sender, () => Task.CompletedTask, CancellationToken.None));
        Assert.True(h.Sender.WaitForPeerCloseAsync(TimeSpan.FromSeconds(5)).IsCompletedSuccessfully,
            "Read failure did not release Close readiness before observer error");
        Assert.Same(failure, h.StatusError); Assert.Same(failure, h.ObserverError);
        Assert.True(h.Events.ToList().IndexOf("Aborted") < h.Events.ToList().IndexOf("observer-error"));
        Assert.False(h.Expire.Task.IsCompleted);
    }

    [Fact]
    public async Task ParserCompletionAwaitsFallbackCloseBeforeObserverCompletion()
    {
        await using var h = new Harness(output); await h.Start(); h.PauseWrite = true;
        h.Source.OnCompleted();
        // A missing finally await either completes the observer or reaches the write.
        await h.Wait(Task.WhenAny(h.WriteEntered.Task, h.Completed.Task));
        Assert.True(h.Close.IsValueCreated, "Parser completion did not start fallback Close");
        Assert.False(h.Completed.Task.IsCompleted, "Observer completion overtook Close write");
        h.Sender.NotifyPeerClose(); h.ResumeWrite.TrySetResult();
        await h.Wait(h.Close.Value);
        await h.Wait(h.Completed.Task);
        Assert.Equal(1, h.TerminalCount); Assert.False(h.Error.Task.IsCompleted);
        Assert.Equal(new[] { 8 }, h.Frames);
        Assert.True(h.Events.ToList().IndexOf("Disconnected") < h.Events.ToList().IndexOf("observer-completed"));
    }

    [Fact]
    public async Task ObserverErrorPrecedesCloseAndNeverBecomesCompletion()
    {
        await using var h = new Harness(output); await h.Start();
        var failure = new IOException("original error");
        var observer = System.Reactive.Observer.Create<IDataframe?>(_ => { }, ex =>
        {
            h.Events.Enqueue("observer-error"); h.ObserverError = ex; h.TerminalCount++;
        }, () => { h.Completed.TrySetResult(); h.TerminalCount++; });
        await h.Wait(h.Connection.ObserveFrames(Observable.Throw<Dataframe?>(failure), observer, h.Sender,
            () => { h.Events.Enqueue("fallback-close"); return Task.CompletedTask; }, CancellationToken.None));
        Assert.True(ReferenceEquals(failure, h.ObserverError), "Observer error was not routed before Close");
        Assert.Same(failure, h.StatusError);
        Assert.Equal(1, h.TerminalCount); Assert.False(h.Completed.Task.IsCompleted);
        var sequence = h.Events.ToList();
        Assert.True(sequence.IndexOf("Aborted") < sequence.IndexOf("observer-error"));
        Assert.True(sequence.IndexOf("observer-error") < sequence.IndexOf("fallback-close"));

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalStopsActivePeriodicPingsBeforeClose(bool synchronous)
    {
        await using var h = new Harness(output); await h.Start(ping: true);
        Assert.True(h.Scheduler.Scheduled > 0, "Periodic timer did not use the injected scheduler");
        h.Scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
        Assert.True(h.WriteEntered.Task.IsCompletedSuccessfully, "Virtual periodic tick did not start Ping");
        Assert.Equal(new[] { 9 }, h.Frames);
        h.Sender.NotifyPeerClose();
        if (synchronous) h.Connection.Dispose(); else await h.Wait(h.Connection.DisposeAsync().AsTask());
        var scheduled = h.Scheduler.Scheduled;
        // Inspect the scheduled subscription's cancellation, not suppressed sender writes:
        // sends after Close could be suppressed even if the periodic timer leaked.
        Assert.Equal(DateTimeOffset.MinValue.AddSeconds(1), h.Scheduler.Clock);
        h.Scheduler.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.True(scheduled == h.Scheduler.Scheduled, "Periodic ping subscription remained active after disposal");
        Assert.Equal(new[] { 9, 8 }, h.Frames);
        Assert.True(!h.Events.Contains("SendError"), "Periodic ping subscription remained active after disposal");
    }

    [Fact]
    public async Task DisposalCancelsPausedPeriodicPingAndReleasesTransport()
    {
        await using var h = new Harness(output); await h.Start(ping: true); h.PauseWrite = true;
        Assert.True(h.Scheduler.Scheduled > 0, "Periodic timer did not use the injected scheduler");
        h.Scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
        Assert.True(h.WriteEntered.Task.IsCompletedSuccessfully, "Virtual periodic tick did not start Ping");
        var pingToken = h.WriteToken;
        var disposal = h.Connection.DisposeAsync().AsTask();
        Assert.True(pingToken.IsCancellationRequested, "Disposal did not cancel the active periodic Ping");
        await h.Wait(disposal);
        Assert.False(h.ResumeWrite.Task.IsCompleted); // cancellation, not a resumed write
        Assert.Equal(new[] { 9 }, h.Frames); // failed/cancelled prefix must not be retried as Close
        Assert.True(h.Disconnected.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CancelledParserLeavesCloseToDisposalAndDoesNotCompleteObserver()
    {
        await using var h = new Harness(output); await h.Start();
        using var cancellation = new CancellationTokenSource();
        int closes = 0, terminals = 0;
        var observer = System.Reactive.Observer.Create<IDataframe?>(_ => { }, _ => terminals++, () => terminals++);
        var pipeline = h.Connection.ObserveFrames(Observable.Never<Dataframe?>(), observer,
            h.Sender, () => { closes++; return Task.CompletedTask; }, cancellation.Token);
        cancellation.Cancel();
        await h.Wait(pipeline);
        Assert.True(closes == 0, "Cancelled parser started fallback Close instead of leaving it to disposal");
        Assert.Equal(0, terminals);
        Assert.False(h.Close.IsValueCreated);
        h.Sender.NotifyPeerClose();
        await h.Wait(h.Connection.DisposeAsync().AsTask());
        Assert.Equal(new[] { 8 }, h.Frames);
    }

    [Fact]
    public async Task ParserCompletionRoutesOneObserverCompletionAfterClose()
    {
        await using var h = new Harness(output); await h.Start();
        int completions = 0;
        var observer = System.Reactive.Observer.Create<IDataframe?>(_ => { }, _ => throw new InvalidOperationException(),
            () => { h.Events.Enqueue("completed"); completions++; });
        await h.Wait(h.Connection.ObserveFrames(Observable.Empty<Dataframe?>(), observer, h.Sender,
            () => { h.Events.Enqueue("fallback-close"); return Task.CompletedTask; }, CancellationToken.None));
        Assert.True(completions == 1, "Parser did not route exactly one observer completion");
        Assert.True(h.Events.ToList().IndexOf("fallback-close") < h.Events.ToList().IndexOf("completed"));
    }

    private sealed class TrackingScheduler : HistoricalScheduler
    {
        internal int Scheduled;
        public override IDisposable ScheduleAbsolute<TState>(TState state, DateTimeOffset dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            Scheduled++;
            return base.ScheduleAbsolute(state, dueTime, action);
        }
    }

    private sealed class HoldingContext : SynchronizationContext
    {
        internal readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Queue = new();
        internal readonly TaskCompletionSource Posted = Signal();
        public override void Post(SendOrPostCallback d, object? state) { Queue.Enqueue((d, state)); Posted.TrySetResult(); }
        internal void Drain() { while (Queue.TryDequeue(out var item)) item.Callback(item.State); }
    }

    [Fact]
    public async Task AsyncDisposalDoesNotRequireCallingSynchronizationContext()
    {
        await using var h = new Harness(output); await h.Start(); h.PauseWrite = true;
        var context = new HoldingContext();
        Task disposal;
        var previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(context); disposal = h.Connection.DisposeAsync().AsTask(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        try
        {
            await h.Wait(h.WriteEntered.Task); Assert.False(disposal.IsCompleted);
            h.Sender.NotifyPeerClose(); h.ResumeWrite.TrySetResult();
            await h.Wait(Task.WhenAny(disposal, context.Posted.Task));
            Assert.True(disposal.IsCompletedSuccessfully, "Disposal captured the calling SynchronizationContext");
            Assert.Empty(context.Queue);
        }
        finally { context.Drain(); await h.Wait(disposal); }
    }
}
