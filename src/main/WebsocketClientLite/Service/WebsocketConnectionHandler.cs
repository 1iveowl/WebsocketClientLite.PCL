using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using IWebsocketClientLite;
using WebsocketClientLite.Model;
using WebsocketClientLite.CustomException;

namespace WebsocketClientLite.Service;

internal class WebsocketConnectionHandler : IDisposable, IAsyncDisposable
{
    private readonly TcpConnectionService _tcpConnectionService;
    private readonly WebsocketParserHandler _websocketParserHandler;
    private readonly Action<ConnectionStatus, Exception?> _connectionStatusAction;
    private readonly Func<Stream, Action<ConnectionStatus, Exception?>, WebsocketSenderHandler> _createWebsocketSenderFunc;

    private IDisposable? _clientPingDisposable;

    // Exactly-once close handshake shared by every teardown path (pipeline
    // completion/error, unsubscribe, Dispose, DisposeAsync). Backed by a
    // Lazy<Task>, so all callers get the SAME task: whichever path fires first
    // starts the close, every other path sees it via IsValueCreated, and
    // awaiting paths await its completion — guaranteeing the close frame gets
    // its chance to go out before the socket is torn down, without racing an
    // in-flight close.
    private Lazy<Task>? _closeHandshakeOnce;

    internal WebsocketConnectionHandler(
        TcpConnectionService tcpConnectionService,
        WebsocketParserHandler websocketParserHandler,
        Action<ConnectionStatus, Exception?> connectionStatusAction,
        Func<Stream, Action<ConnectionStatus, Exception?>, WebsocketSenderHandler> createWebsocketSenderFunc)
    {
        _tcpConnectionService = tcpConnectionService;            
        _websocketParserHandler = websocketParserHandler;
        _connectionStatusAction = connectionStatusAction;
        _createWebsocketSenderFunc = createWebsocketSenderFunc;

        _clientPingDisposable = default;
    }

    internal async Task<IObservable<IDataframe?>>
            ConnectWebsocket(
                Uri uri,
                X509CertificateCollection? x509CertificateCollection,
                SslProtocols tlsProtocolType,
                Action<ISender, IEnumerable<string>?> setSenderAction,
                bool hasClientPing,
                TimeSpan clientPingTimeSpan,
                string? clientPingMessage,
                TimeSpan timeout,
                string? origin,
                IDictionary<string, string>? headers,
                IEnumerable<string>? subprotocols,
                CancellationToken ct,
                CancellationToken cancellationToken = default)
    {
        if (hasClientPing && clientPingTimeSpan == default)
        {
            clientPingTimeSpan = TimeSpan.FromSeconds(30);
        }

        await _tcpConnectionService.ConnectTcpStream(
            uri,
            x509CertificateCollection,
            tlsProtocolType,
            timeout).ConfigureAwait(false);

        var sender = _createWebsocketSenderFunc(
            _tcpConnectionService.ConnectionStream,
            _connectionStatusAction);

        var handshakeHandler = new HandshakeHandler(
                _tcpConnectionService,
                _connectionStatusAction);

        var (handshakeState, handshakeException) = 
            await handshakeHandler.Handshake(uri, sender, timeout, ct, origin, headers, subprotocols);

        if(handshakeException is not null)
        {
            throw handshakeException;
        }
        else if (handshakeState is HandshakeStateKind.HandshakeCompletedSuccessfully)
        {
            _connectionStatusAction(ConnectionStatus.HandshakeCompletedSuccessfully, null);              
        }
        else
        {
            throw new WebsocketClientLiteException($"Handshake failed due to unknown error: {handshakeState}");
        }

        setSenderAction(sender, handshakeHandler.NegotiatedSubprotocols);

        if (hasClientPing)
        {
            _clientPingDisposable = SendClientPing(clientPingMessage)
                .Subscribe(
                _ => { },
                // Throwing here would rethrow on the Interval scheduler thread as an
                // unhandled exception. Surface it through the status callback instead;
                // connection teardown is driven by the read side.
                ex => _connectionStatusAction(
                    ConnectionStatus.SendError,
                    new WebsocketClientLiteException("Sending client ping failed.", ex)),
                () => { });
        }

        // The close handshake must run exactly once, whichever teardown path
        // fires first: pipeline completion/error (the finally below), or disposal
        // (Dispose/DisposeAsync, reached on unsubscribe through ws.Dispose()).
        // Lazy hands every path the same task, so "exactly once" and "await the
        // in-flight close" come from one mechanism. The hook is registered BEFORE
        // WebsocketConnected is emitted so even a subscriber that disposes
        // immediately upon "connected" — possibly before the pipeline
        // subscription exists — still triggers the close handshake.
        // Task.Run keeps the factory side-effect-free and instantly returning:
        // DisconnectWebsocket must NOT start synchronously inside the Lazy
        // factory, because on a fast (e.g. loopback) socket the whole send —
        // including the Disconnected status callback and the unsubscribe
        // cascade it triggers — would run while the Lazy lock is held and
        // IsValueCreated is still false, deadlocking a re-entrant Dispose. It
        // also keeps the close off the caller's synchronization context.
        var closeOnce = new Lazy<Task>(
            () => Task.Run(() => DisconnectWebsocket(sender)),
            LazyThreadSafetyMode.ExecutionAndPublication);

        Task CloseOnceAsync() => closeOnce.Value;

        _closeHandshakeOnce = closeOnce;

        _connectionStatusAction(ConnectionStatus.WebsocketConnected, null);

        // All three end paths — source completion, source error, and unsubscribe
        // (the token fires on disposal) — converge on the finally below, so the
        // close handshake runs before the terminal event reaches the downstream
        // teardown (ws.Dispose) on the completion path, while the disposal paths
        // are ordered by Dispose/DisposeAsync waiting on the same shared task.
        return Observable.Create<IDataframe?>(async (dataframeObserver, ct) =>
        {
            Exception? sourceError = null;

            try
            {
                var terminated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cancellationRegistration = ct.Register(() => terminated.TrySetResult(true));

                // DataframeObservable emits every message from a single
                // subscription, so no .Repeat() (and its per-message
                // re-subscription) is needed.
                using var subscription = _websocketParserHandler.DataframeObservable()
                    .SelectMany(async dataframe =>
                        // Process control frames and return data frames
                        await IncomingControlFrameHandler(dataframe, cancellationToken).ConfigureAwait(false))
                    .Where(dataframe => dataframe is not null)
                    .Subscribe(
                        dataframeObserver.OnNext,
                        ex => { sourceError = ex; terminated.TrySetResult(true); },
                        () => terminated.TrySetResult(true));

                await terminated.Task.ConfigureAwait(false);

                // Surface the error BEFORE the close handshake, and through the
                // STATUS channel (Aborted -> OnError): the subscriber's stream
                // is fed by two independent branches (status and dataframes)
                // with no ordering between them, so an error forwarded only on
                // the dataframe branch races the status stream's completion
                // (Disconnected -> OnCompleted, emitted inside the close) and
                // can be swallowed. Routing it via the status observer makes
                // the error causally precede anything the close produces. The
                // dataframe-branch OnError below is belt-and-braces; a second
                // terminal on the subscriber is suppressed by Rx.
                if (sourceError is not null)
                {
                    _connectionStatusAction(ConnectionStatus.Aborted, sourceError);
                    dataframeObserver.OnError(sourceError);
                }
            }
            finally
            {
                // Completion/error paths: run (or await) the close here, before
                // the terminal event reaches the downstream teardown. On the
                // unsubscribe path (token cancelled) the disposal cascade owns
                // the close instead — Dispose/DisposeAsync start it and wait
                // before touching the socket — so starting it here as well
                // would only race that cascade.
                if (!ct.IsCancellationRequested)
                {
                    await CloseOnceAsync().ConfigureAwait(false);
                }
            }

            if (sourceError is null && !ct.IsCancellationRequested)
            {
                dataframeObserver.OnCompleted();
            }
        });

        IObservable<Unit> SendClientPing(string? message) =>
            Observable.Interval(clientPingTimeSpan)
            .Select(_ => Observable.FromAsync(ct => sender.SendPing(message, ct)))
            .Concat();

        async Task<Dataframe?> IncomingControlFrameHandler(
            Dataframe? dataframe,
            CancellationToken ct)
        {
            return dataframe?.Opcode switch
            {
                // Data frames that should be passed through
                OpcodeKind.Continuation or
                OpcodeKind.Text or
                OpcodeKind.Binary => dataframe,

                // Control frames that require special handling
                OpcodeKind.Ping => await HandlePing().ConfigureAwait(false),
                OpcodeKind.Pong => HandlePong(),
                OpcodeKind.Close => HandleClose(),

                // Reserved opcodes - throw not implemented
                OpcodeKind.Reserved1 or
                OpcodeKind.Reserved2 or
                OpcodeKind.Reserved3 or
                OpcodeKind.Reserved4 or
                OpcodeKind.Reserved5 or
                OpcodeKind.Reserved1a or
                OpcodeKind.Reserved2b or
                OpcodeKind.Reserved3c or
                OpcodeKind.Reserved4d or
                OpcodeKind.Reserved5e => throw new NotImplementedException($"Opcode not implemented: {dataframe.Opcode}"),

                // Default case (null or unhandled)
                _ => throw new ArgumentOutOfRangeException($"{dataframe?.Opcode}")
            };

            // Local functions to handle specific control frames
            async Task<Dataframe?> HandlePing()
            {
                _connectionStatusAction(ConnectionStatus.PingReceived, null);
                await sender.SendPong(dataframe!, ct).ConfigureAwait(false);
                return null;
            }

            Dataframe? HandlePong()
            {
                _connectionStatusAction(ConnectionStatus.PongReceived, null);
                return null;
            }

            Dataframe? HandleClose()
            {
                // No explicit completion here: the read loop stops right after a
                // Close frame (both the main loop and reassembly), so the source
                // observable completes and the teardown finally takes over.
                _connectionStatusAction(ConnectionStatus.Close, null);
                return null;
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The close frame is a best-effort courtesy. Whatever the failure type (dead stream, " +
                        "timeout, disposed socket), it must not mask the error that triggered the teardown — " +
                        "send failures otherwise throw at the call site by design.")]
    internal async Task DisconnectWebsocket(
        WebsocketSenderHandler sender)
    {
        try
        {
            await sender.SendCloseHandshakeAsync(StatusCodes.GoingAway)
                .ToObservable()
                .Timeout(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            // Best effort: the close frame is a courtesy. Failing to deliver it
            // (dead stream, timeout) must not mask the error that triggered the
            // teardown — especially now that send failures throw.
            Debug.WriteLine($"Close handshake could not be delivered: {ex.Message}");
        }
        finally
        {
            _connectionStatusAction(ConnectionStatus.Disconnected, null);
        }
    }

    /// <summary>
    /// Graceful teardown: awaits the (shared, exactly-once) close handshake —
    /// starting it if no other path has yet — and only then releases the parser
    /// and TCP resources. The close itself is bounded internally
    /// (<see cref="DisconnectWebsocket"/> times out and never throws), so this
    /// completes in finite time.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Stop the ping timer before saying goodbye.
        _clientPingDisposable?.Dispose();

        var closeOnce = _closeHandshakeOnce;
        if (closeOnce is not null)
        {
            await closeOnce.Value.ConfigureAwait(false);
        }

        _websocketParserHandler?.Dispose();
        _tcpConnectionService?.Dispose();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Dispose must never throw. The guarded block is the bounded best-effort close " +
                        "handshake; any failure there is irrelevant because the connection is being torn down.")]
    public void Dispose()
    {
        // Stop the ping timer before saying goodbye.
        _clientPingDisposable?.Dispose();

        // Abrupt (bounded best-effort) teardown: ws.Dispose() (the outer
        // Finally) is the one hook guaranteed to run no matter how early the
        // subscriber disposes, so the close handshake is started here, BEFORE
        // TcpConnectionService is disposed below. Bounded so disposal cannot
        // hang; the close runs on the thread pool (the Lazy factory queues it),
        // so callers disposing from a synchronization context (e.g. UI) cannot
        // deadlock. Prefer DisposeAsync for a fully awaited close.
        //
        // Only start-and-wait when no other path has started the close yet
        // (IsValueCreated). When it is already in flight, the frame send is
        // causally ordered before whatever terminal event led here — and this
        // Dispose may in fact be re-entered from the close task's own
        // completion cascade (Disconnected status -> stream completion ->
        // unsubscribe -> ws.Dispose), where waiting would deadlock until the
        // bound expires.
        var closeOnce = _closeHandshakeOnce;
        if (closeOnce is not null && !closeOnce.IsValueCreated)
        {
            try
            {
                // Starting the Lazy is cheap and non-blocking (the factory just
                // queues the close to the thread pool), so the bound applies to
                // the close itself.
                var closeTask = closeOnce.Value;
                if (!closeTask.Wait(TimeSpan.FromSeconds(3)))
                {
                    _ = closeTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                }
            }
            catch
            {
                // Best effort — the connection is going away regardless.
            }
        }

        _websocketParserHandler?.Dispose();
        _tcpConnectionService?.Dispose();
    }
}