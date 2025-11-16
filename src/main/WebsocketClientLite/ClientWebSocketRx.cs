using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using WebsocketClientLite.Factory;
using IWebsocketClientLite;
using WebsocketClientLite.Service;

namespace WebsocketClientLite;

public record ClientWebSocketRx : IWebSocketClientRx, IDisposable, IEquatable<ClientWebSocketRx>
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IObserver<bool> _observerIsConnected;
    private readonly EventLoopScheduler _eventLoopScheduler = new();

    public bool HasTransferSocketLifeCycleOwnership { get; init; }

    public TcpClient TcpClient { get; init; } = new TcpClient();

    /// <summary>
    /// Websocket client connected observable.
    /// </summary>
    public IObservable<bool> IsConnectedObservable { get; }

    /// <summary>
    /// Get websocket client sender.
    /// </summary>
    /// <returns></returns>
    public ISender? Sender { get; private set; }

    /// <summary>
    /// Websocket client connected event.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Http Headers
    /// </summary>
    public IDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Websocket known subprotocols
    /// </summary>
    public IEnumerable<string>? Subprotocols { get; init; }

    /// <summary>
    /// TLS protocol
    /// </summary>
    public SslProtocols TlsProtocolType { get; init; } = SslProtocols.None;

    /// <summary>
    /// Typically used with Slack. See documentation.
    /// </summary>
    public bool ExcludeZeroApplicationDataInPong { get; init; }

    /// <summary>
    /// Use with care. Ignores TLS/SSL certificate checks and errors. See documentation.
    /// </summary>
    public bool IgnoreServerCertificateErrors { get; init; }

    /// <summary>
    /// X.509 certificate collection.
    /// </summary>
    public X509CertificateCollection? X509CertCollection { get; init; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ClientWebSocketRx()
    {
        BehaviorSubject<bool> connectedBehavior = new(false);
        _observerIsConnected = connectedBehavior.AsObserver();

        IsConnectedObservable = connectedBehavior.AsObservable();

        // Register the BehaviorSubject with the disposer
        _disposables.Add(connectedBehavior);
    }


    /// <summary>
    /// Is using a secure connection scheme. Override for anything but default behavior.
    /// </summary>
    /// <param name="uri">Secure connection scheme method. Override for anything but default behavior.</param>
    /// <returns></returns>
    public virtual bool IsSecureConnectionScheme(Uri uri) =>
        uri?.Scheme switch
        {
            "ws" or "http" => false,
            "https" or "wss" => true,
            _ => throw new ArgumentException("Unknown Uri type.")
        };

    /// <summary>
    /// Server certificate validation. Override for anything but default behavior.
    /// </summary>
    /// <param name="senderObject">Sender object</param>
    /// <param name="certificate">X.509 Certificate</param>
    /// <param name="chain">X.509 Chain</param>
    /// <param name="TlsPolicyErrors"> TLS/SSL policy Errors</param>
    /// <returns></returns>
    public virtual bool ValidateServerCertificate(
        object senderObject,
        X509Certificate certificate,
        X509Chain chain,
        SslPolicyErrors TlsPolicyErrors)
    {
        if (IgnoreServerCertificateErrors) return true;

        return TlsPolicyErrors switch
        {
            SslPolicyErrors.None => true,
            SslPolicyErrors.RemoteCertificateChainErrors =>
                throw new AuthenticationException($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors}"),
            SslPolicyErrors.RemoteCertificateNameMismatch =>
                throw new AuthenticationException($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNameMismatch}"),
            SslPolicyErrors.RemoteCertificateNotAvailable =>
                throw new AuthenticationException($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNotAvailable}"),
            _ => throw new ArgumentOutOfRangeException(nameof(TlsPolicyErrors), TlsPolicyErrors, null),
        };
    }

    /// <summary>
    /// Websocket Connection Observable.
    /// </summary>
    /// <param name="uri">Websocket Server Endpoint (URI).</param>
    /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
    /// <param name="clientPingInterval">Specific client ping interval. Default is 30 seconds.</param>
    /// <param name="clientPingMessage">Default none. Specific client message. Will stay constant and can only be a <see langword="string"/>. For more advanced scenarios use <see cref="ISender.SendPing"/></param>
    /// <param name="handshaketimeout">Specific time out for client trying to connect (aka handshake). Default is 30 seconds.</param>
    /// <returns></returns>
    public IObservable<IDataframe?> WebsocketConnectObservable(
        Uri uri,
        bool hasClientPing = false,
        TimeSpan clientPingInterval = default,
        string? clientPingMessage = default,
        TimeSpan handshaketimeout = default,
        CancellationToken cancellationToken = default) =>
            WebsocketConnectWithStatusObservable(uri, hasClientPing, clientPingInterval, clientPingMessage, handshaketimeout)
                .Where(tuple => tuple.state is ConnectionStatus.DataframeReceived)
                .Select(tuple => tuple.dataframe);

    /// <summary>
    /// Websocket Connection Observable with status.
    /// </summary>
    /// <param name="uri">Websocket Server Endpoint (URI).</param>
    /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
    /// <param name="clientPingInterval">Specific client ping interval. Default is 30 seconds will be used.</param>
    /// <param name="clientPingMessage">Specific client message. Default none. Will stay constant and can only be a <see langword="string"/>. For more advanced scenarios use <see cref="ISender.SendPing"/></param>
    /// <param name="handshaketimeout">Specific time out for client trying to connect (aka handshake). Default is 30 seconds.</param>
    /// <returns></returns>
    public IObservable<(IDataframe? dataframe, ConnectionStatus state)>
        WebsocketConnectWithStatusObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingInterval = default,
            string? clientPingMessage = default,
            TimeSpan handshaketimeout = default,
            CancellationToken cancellationToken = default)
    {
        if (handshaketimeout == default)
        {
            handshaketimeout = TimeSpan.FromSeconds(30);
        }

        void initSender(ISender sender)
        {
            Sender = sender;
            _observerIsConnected.OnNext(true);
        }

        return Observable.Create<(IDataframe? dataframe, ConnectionStatus state)>(obsTuple =>
            Observable.Create<ConnectionStatus>(async obsStatus =>
            {
                obsStatus.OnNext(ConnectionStatus.Initialized);

                return await Observable.FromAsync<WebsocketService>(_ => WebsocketClientFactory.Create(
                            () => IsSecureConnectionScheme(uri),
                            ValidateServerCertificate,
                            _eventLoopScheduler,
                            obsStatus,
                            this))
                        .Select(ws => Observable.FromAsync<IObservable<IDataframe?>>(ct => ConnectWebsocket(ws, ct))
                            .Concat()
                            .Finally(() => ws.Dispose())
                            .Subscribe(
                                dataframe => { obsTuple.OnNext((dataframe, ConnectionStatus.DataframeReceived)); },
                                ex => { obsTuple.OnError(ex); },
                                () => { obsTuple.OnCompleted(); }));
            })
            .Finally(() => _observerIsConnected.OnNext(false))
            .Subscribe(
                state => { obsTuple.OnNext((null, state)); },
                ex => { obsTuple.OnError(ex); },
                () => { obsTuple.OnCompleted(); })
            );

        async Task<IObservable<IDataframe?>> ConnectWebsocket(WebsocketService ws, CancellationToken ct) =>
            await ws.WebsocketConnectionHandler.ConnectWebsocket(
                uri,
                X509CertCollection,
                TlsProtocolType,
                initSender,
                ct,
                hasClientPing,
                clientPingInterval,
                clientPingMessage,
                handshaketimeout,
                Origin,
                Headers,
                Subprotocols,
                cancellationToken).ConfigureAwait(false);
    }

    // Add a protected virtual Dispose(bool disposing) method
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposables.Dispose();
            _eventLoopScheduler.Dispose();
        }
    }

    // Update Dispose() to call Dispose(true) and GC.SuppressFinalize(this)
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
