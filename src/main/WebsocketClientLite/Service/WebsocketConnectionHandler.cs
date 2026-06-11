using System;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using IWebsocketClientLite;
using WebsocketClientLite.Extension;
using WebsocketClientLite.Model;
using WebsocketClientLite.CustomException;

namespace WebsocketClientLite.Service;

internal class WebsocketConnectionHandler : IDisposable
{
    private readonly TcpConnectionService _tcpConnectionService;
    private readonly WebsocketParserHandler _websocketParserHandler;
    private readonly Action<ConnectionStatus, Exception?> _connectionStatusAction;
    private readonly Func<Stream, Action<ConnectionStatus, Exception?>, WebsocketSenderHandler> _createWebsocketSenderFunc;

    private IDisposable? _clientPingDisposable;

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
                CancellationToken ct,
                bool hasClientPing,
                TimeSpan clientPingTimeSpan,
                string? clientPingMessage,
                TimeSpan timeout,
                string? origin,
                IDictionary<string, string>? headers,
                IEnumerable<string>? subprotocols,
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

        _connectionStatusAction(ConnectionStatus.WebsocketConnected, null);

        // The close handshake must run exactly once, whichever teardown path
        // fires first: completion/error (via FinallyAsync) or the subscriber
        // disposing the subscription (via the Disposable below, which previously
        // sent no close frame at all).
        int closeHandshakeStarted = 0;

        async Task CloseOnceAsync()
        {
            if (Interlocked.Exchange(ref closeHandshakeStarted, 1) == 0)
            {
                await DisconnectWebsocket(sender).ConfigureAwait(false);
            }
        }

        return Observable.Create<IDataframe?>(dataframeObserver =>
        {
            // DataframeObservable emits every message from a single
            // subscription, so no .Repeat() (and its per-message re-subscription)
            // is needed.
            var subscription = _websocketParserHandler.DataframeObservable()
                .SelectMany(async dataframe =>
                    // Process control frames and return data frames
                    await IncomingControlFrameHandler(dataframe, dataframeObserver, cancellationToken).ConfigureAwait(false))
                .Where(dataframe => dataframe is not null)
                .Subscribe(dataframeObserver);

            return new CompositeDisposable(
                subscription,
                Disposable.Create(() =>
                {
                    // Unsubscribe path: best-effort close handshake, bounded so
                    // disposal cannot hang. Task.Run avoids deadlocking callers
                    // that dispose from a synchronization context (e.g. UI).
                    try
                    {
                        var closeTask = Task.Run(CloseOnceAsync);
                        if (!closeTask.Wait(TimeSpan.FromSeconds(3)))
                        {
                            _ = closeTask.ContinueWith(
                                t => _ = t.Exception,
                                TaskScheduler.Default);
                        }
                    }
                    catch
                    {
                        // The connection is going away regardless.
                    }
                }));
        })
        .FinallyAsync(CloseOnceAsync);

        IObservable<Unit> SendClientPing(string? message) =>
            Observable.Interval(clientPingTimeSpan)
            .Select(_ => Observable.FromAsync(ct => sender.SendPing(message, ct)))
            .Concat();

        async Task<Dataframe?> IncomingControlFrameHandler(
            Dataframe? dataframe, 
            IObserver<Dataframe?> obs, 
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
                _connectionStatusAction(ConnectionStatus.Close, null);
                obs.OnCompleted();
                return null;
            }
        }
    }

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

    public void Dispose()
    {
        _clientPingDisposable?.Dispose();
        _websocketParserHandler?.Dispose();
        _tcpConnectionService?.Dispose();
    }
}