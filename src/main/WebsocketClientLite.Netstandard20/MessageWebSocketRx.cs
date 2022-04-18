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

        internal TcpClient TcpClient { get; private set; }

        private ISender _sender;

        /// <summary>
        /// Is Websocket client connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Origin.
        /// </summary>
        public string Origin { get; set; }

        /// <summary>
        /// Http Headers.
        /// </summary>
        public IDictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Websocket known subprotocols.
        /// </summary>
        public IEnumerable<string> Subprotocols { get; set; }


        /// <summary>
        /// TLS protocol.
        /// </summary>
        public SslProtocols TlsProtocolType { get; set; }

        /// <summary>
        /// X.509 certificate collection.
        /// </summary>
        public X509CertificateCollection X509CertCollection { get; set; }

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
            ? _sender 
            : throw new InvalidOperationException("No sender available, Websocket not connected. You need to subscribe to WebsocketConnectObservable first.");
        
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="tcpClient"></param>
        public MessageWebsocketRx(TcpClient tcpClient) 
        {
            _eventLoopScheduler = new EventLoopScheduler();

            TcpClient = tcpClient;
            Subprotocols = null;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public MessageWebsocketRx() : this(null)
        {
            
        }

        /// <summary>
        /// Websocket Connection Observable.
        /// </summary>
        /// <param name="uri">Websocket Server Endpoint (URI).</param>
        /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
        /// <param name="clientPingTimeSpan">Specific client ping interval. Default is 30 seconds.</param>
        /// <param name="timeout">Specific time out for client trying to connect. Default is 30 seconds.</param>
        /// <returns></returns>
        public IObservable<IDataframe> WebsocketConnectObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingTimeSpan = default,
            TimeSpan timeout = default) =>
                WebsocketConnectWithStatusObservable(uri, hasClientPing, clientPingTimeSpan, timeout)
                    .Where(tuple => tuple.state is ConnectionStatus.DataframeReceived)
                    .Select(tuple => tuple.dataframe);

        /// <summary>
        /// Websocket Connection Observable with status.
        /// </summary>
        /// <param name="uri">Websocket Server Endpoint (URI).</param>
        /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
        /// <param name="clientPingTimeSpan">Specific client ping interval. Default is 30 seconds will be used.</param>
        /// <param name="timeout">Specific time out for client trying to connect. Default is 30 seconds.</param>
        /// <returns></returns>
        public IObservable<(IDataframe dataframe, ConnectionStatus state)>
            WebsocketConnectWithStatusObservable (
                Uri uri,
                bool hasClientPing = false,
                TimeSpan clientPingTimeSpan = default,
                TimeSpan timeout = default)
        {
            if (timeout == default)
            {
                timeout = TimeSpan.FromSeconds(30);
            }

            return Observable.Create<(IDataframe dataframe, ConnectionStatus state)>(obsTuple =>
            {
                return Observable.Create<ConnectionStatus>(obsStatus =>
                {
                    obsStatus.OnNext(ConnectionStatus.Initialized);

                    return Observable.Using(
                        resourceFactoryAsync: cts => WebsocketServiceFactory.Create(
                             () => IsSecureConnectionScheme(uri),
                             ValidateServerCertificate,
                             _eventLoopScheduler,
                             obsStatus,
                             this),
                        observableFactoryAsync: (websocketServices, ct) =>
                            Task.FromResult(Observable.Return(websocketServices)))
                                .Select(websocketService => Observable
                                    .FromAsync(ct => ConnectWebsocket(websocketService, ct)).Concat())
                                .Concat()
                                .Subscribe(
                                    dataframe => { obsTuple.OnNext((dataframe, ConnectionStatus.DataframeReceived)); },
                                    ex => { obsTuple.OnError(ex); },
                                    () => { obsTuple.OnCompleted(); });
                })
                .Finally(() => IsConnected = false)
                .Subscribe(
                    state =>{ obsTuple.OnNext((null, state));},
                    ex => { obsTuple.OnError(ex); },
                    () => { obsTuple.OnCompleted(); });
            });

            async Task<IObservable<IDataframe>> ConnectWebsocket(WebsocketService ws, CancellationToken ct) =>
                await ws.WebsocketConnectionHandler.ConnectWebsocket(
                    uri,
                    X509CertCollection,
                    TlsProtocolType,
                    InitializeSender,
                    ct,
                    hasClientPing,
                    clientPingTimeSpan,                                    
                    timeout,
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

        }
    }
}
