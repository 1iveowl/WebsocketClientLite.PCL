using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Factory;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL
{
    /// <summary>
    /// Websocket Client Lite.
    /// </summary>
    public class MessageWebsocketRx : IMessageWebSocketRx
    {
        private readonly EventLoopScheduler _eventLoopScheduler;

        internal TcpClient? TcpClient { get; private set; }
        internal bool HasTransferSocketLifeCycleOwnership { get; private set; }

        private ISender? _sender;

        /// <summary>
        /// Is Websocket client connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Origin.
        /// </summary>
        public string? Origin { get; set; }

        /// <summary>
        /// Http Headers.
        /// </summary>
        public IDictionary<string, string>? Headers { get; set; }

        /// <summary>
        /// Websocket known subprotocols.
        /// </summary>
        public IEnumerable<string>? Subprotocols { get; set; }


        /// <summary>
        /// TLS protocol.
        /// </summary>
        public SslProtocols TlsProtocolType { get; set; }

        /// <summary>
        /// X.509 certificate collection.
        /// </summary>
        public X509CertificateCollection? X509CertCollection { get; set; }

        /// <summary>
        /// Typically used with Slack. See documentation. 
        /// </summary>
        public bool ExcludeZeroApplicationDataInPong { get; set; }

        /// <summary>
        /// Use with care. Ignores TLS/SSL certificate checks and errors. See documentation.
        /// </summary>
        public bool IgnoreServerCertificateErrors { get; set; }

        /// <summary>
        /// Get websocket client sender.
        /// </summary>
        /// <returns></returns>
        public ISender GetSender() => IsConnected 
            ? _sender is not null ? _sender : throw new NullReferenceException("Sender not initialized.")
            : throw new InvalidOperationException("No sender available, Websocket not connected. You need to subscribe to WebsocketConnectObservable first.");

        /// <summary>
        /// Constructor used when passing a TCP socket. If the TCP socket is not already connected it will be connected using the URI supplied when subscribing to one of the observable WebSockets: <see cref="WebsocketConnectObservable(Uri, bool, TimeSpan, string, TimeSpan)"/> or <see cref="WebsocketConnectWithStatusObservable(Uri, bool, TimeSpan, string, TimeSpan)"/>. 
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="hasTransferTcpSocketLifeCycleOwnership">Default <see cref="false"/>. Set to <see cref="true"/> to transfer ownership of the lifecycle of the TCP Socket to the WebSocket Client.</param>
        public MessageWebsocketRx(
            TcpClient? tcpClient, 
            bool hasTransferTcpSocketLifeCycleOwnership = false)
        {
            _eventLoopScheduler = new EventLoopScheduler();

            HasTransferSocketLifeCycleOwnership = hasTransferTcpSocketLifeCycleOwnership;
            TcpClient = tcpClient;

            Subprotocols = default;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public MessageWebsocketRx() : this(null, true)
        {
            
        }

        /// <summary>
        /// Websocket Connection Observable.
        /// </summary>
        /// <param name="uri">Websocket Server Endpoint (URI).</param>
        /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
        /// <param name="clientPingInterval">Specific client ping interval. Default is 30 seconds.</param>
        /// <param name="clientPingMessage">Default none. Specific client message. Will stay constant and can only be a <see langword="string"/>. For more advanced scenarios use <see cref="IWebsocketClientLite.PCL.ISender.SendPing"/></param>
        /// <param name="handshaketimeout">Specific time out for client trying to connect (aka handshake). Default is 30 seconds.</param>
        /// <returns></returns>
        public IObservable<IDataframe?> WebsocketConnectObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingInterval = default,
            string? clientPingMessage = default,
            TimeSpan handshaketimeout = default) =>
                WebsocketConnectWithStatusObservable(uri, hasClientPing, clientPingInterval, clientPingMessage, handshaketimeout)
                    .Where(tuple => tuple.state is ConnectionStatus.DataframeReceived)
                    .Select(tuple => tuple.dataframe);

        /// <summary>
        /// Websocket Connection Observable with status.
        /// </summary>
        /// <param name="uri">Websocket Server Endpoint (URI).</param>
        /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
        /// <param name="clientPingInterval">Specific client ping interval. Default is 30 seconds will be used.</param>
        /// <param name="clientPingMessage">Specific client message. Default none. Will stay constant and can only be a <see langword="string"/>. For more advanced scenarios use <see cref="IWebsocketClientLite.PCL.ISender.SendPing"/></param>
        /// <param name="handshakeTimeout">Specific time out for client trying to connect (aka handshake). Default is 30 seconds.</param>
        /// <returns></returns>
        public IObservable<(IDataframe? dataframe, ConnectionStatus state)>
            WebsocketConnectWithStatusObservable (
                Uri uri,
                bool hasClientPing = false,
                TimeSpan clientPingInterval = default,
                string? clientPingMessage = default,
                TimeSpan handshakeTimeout = default)
        {
            if (handshakeTimeout == default)
            {
                handshakeTimeout = TimeSpan.FromSeconds(30);
            }

            return Observable.Create<(IDataframe? dataframe, ConnectionStatus state)>(obsTuple =>
                Observable.Create<ConnectionStatus>(async obsStatus =>
                {
                    obsStatus.OnNext(ConnectionStatus.Initialized);

                    return await Observable.FromAsync(ct => WebsocketServiceFactory.Create(
                                () => IsSecureConnectionScheme(uri),
                                ValidateServerCertificate,
                                _eventLoopScheduler,
                                obsStatus,
                                this))
                            .Select(ws => Observable.FromAsync(ct => ConnectWebsocket(ws, ct))
                                .Concat()
                                .Finally(() => ws.Dispose())
                                .Subscribe(
                                    dataframe => { obsTuple.OnNext((dataframe, ConnectionStatus.DataframeReceived)); },
                                    ex => { obsTuple.OnError(ex); },
                                    () => { obsTuple.OnCompleted(); }));
                })
                .Finally(() => IsConnected = false)
                .Subscribe(
                    state =>{ obsTuple.OnNext((null, state));},
                    ex => { obsTuple.OnError(ex); },
                    () => { obsTuple.OnCompleted(); })
                );

            async Task<IObservable<IDataframe?>> ConnectWebsocket(WebsocketService ws, CancellationToken ct) =>
                await ws.WebsocketConnectionHandler.ConnectWebsocket(
                                                    uri,
                                                    X509CertCollection,
                                                    TlsProtocolType,
                                                    InitializeSender,
                                                    ct,
                                                    hasClientPing,
                                                    clientPingInterval,
                                                    clientPingMessage,
                                                    handshakeTimeout,
                                                    Origin,
                                                    Headers,
                                                    Subprotocols);

            void InitializeSender(ISender sender)
            {
                _sender = sender;
                IsConnected = true;
            }
        }

        /// <summary>
        /// Is using a secure connection scheme. Override for anything but default behavior.
        /// </summary>
        /// <param name="uri">Secure connection scheme method. Override for anything but default behavior.</param>
        /// <returns></returns>
        public virtual bool IsSecureConnectionScheme(Uri uri) => 
            uri.Scheme switch
            {
                "ws" or "http" => false,
                "https" or "wss"=> true,
                _ => throw new ArgumentException("Unknown Uri type.")
            };

        /// <summary>
        /// Server certificate validation. Override for anything but default behavior.
        /// </summary>
        /// <param name="senderObject">Sender object</param>
        /// <param name="certificate">X.509 Certificate</param>
        /// <param name="chain">X.509 Chain</param>
        /// <param name="tlsPolicyErrors"> TLS/SSL policy Errors</param>
        /// <returns></returns>
        public virtual bool ValidateServerCertificate(
            object senderObject,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors tlsPolicyErrors)
        {
            if (IgnoreServerCertificateErrors) return true;

            return tlsPolicyErrors switch
            {
                SslPolicyErrors.None => true,
                SslPolicyErrors.RemoteCertificateChainErrors => 
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors}"),
                SslPolicyErrors.RemoteCertificateNameMismatch => 
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNameMismatch}"),
                SslPolicyErrors.RemoteCertificateNotAvailable => 
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNotAvailable}"),
                _ => throw new ArgumentOutOfRangeException(nameof(tlsPolicyErrors), tlsPolicyErrors, null),
            };
        }

        public void Dispose()
        {
            _eventLoopScheduler?.Dispose();
        }
    }
}
