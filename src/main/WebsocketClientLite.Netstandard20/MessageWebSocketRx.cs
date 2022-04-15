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
    public class MessageWebsocketRx : IMessageWebSocketRx
    {
        // private readonly BehaviorSubject<ConnectionStatus> _connectionStatusSubject;
        private readonly EventLoopScheduler _eventLoopScheduler;

        internal TcpClient TcpClient { get; private set; }

        private ISender _sender;

        public bool IsConnected { get; private set; }

        public bool SubprotocolAccepted { get; set; }

        public string Origin { get; set; }

        public IDictionary<string, string> Headers { get; set; }

        public IEnumerable<string> Subprotocols { get; set; }

        public SslProtocols TlsProtocolType { get; set; }

        public X509CertificateCollection X509CertCollection { get; set; }

        public bool ExcludeZeroApplicationDataInPong { get; set; }

        public bool IgnoreServerCertificateErrors { get; set; }

        public ISender GetSender() => IsConnected 
            ? _sender 
            : throw new InvalidOperationException("No sender available, Websocket not connected. You need to subscribe to WebsocketConnectObservable first.");

        //public IObservable<ConnectionStatus> ConnectionStatusObservable { get; private set; } 
                
        public MessageWebsocketRx(TcpClient tcpClient) 
        {
            _eventLoopScheduler = new EventLoopScheduler();

            TcpClient = tcpClient;
            Subprotocols = null;

            //_connectionStatusSubject = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);

            //ConnectionStatusObservable = _connectionStatusSubject
            //    .AsObservable()
            //    .ObserveOn(_eventLoopScheduler);
        }

        public MessageWebsocketRx() : this(null)
        {
            
        }

        public IObservable<IDatagram> WebsocketConnectObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingTimeSpan = default,
            TimeSpan timeout = default) =>
                WebsocketConnectWithStatusObservable(uri, hasClientPing, clientPingTimeSpan, timeout)
                    .Where(tuple => tuple.state == ConnectionStatus.DatagramReceived)
                    .Select(tuple => tuple.datagram);

        public IObservable<(IDatagram datagram, ConnectionStatus state)>
            WebsocketConnectWithStatusObservable (
                Uri uri,
                bool hasClientPing = false,
                TimeSpan clientPingTimeSpan = default,
                TimeSpan timeout = default)
        {
            return Observable.Create<(IDatagram datagram, ConnectionStatus state)>(obsTuple =>
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
                                    datagram => { obsTuple.OnNext((datagram, ConnectionStatus.DatagramReceived)); },
                                    ex => { obsTuple.OnError(ex); },
                                    () => { obsTuple.OnCompleted(); });
                        })
                    .Subscribe(
                        state =>{ obsTuple.OnNext((null, state));},
                        ex => { obsTuple.OnError(ex); },
                        () => { obsTuple.OnCompleted(); });
            });

            //var observableListener = Observable.Using(
            //    resourceFactoryAsync: cts => WebsocketServiceFactory.Create(
            //         () => IsSecureConnectionScheme(uri),
            //         ValidateServerCertificate,
            //         _eventLoopScheduler,
            //         _connectionStatusSubject.AsObserver(),
            //         this),
            //    observableFactoryAsync: (websocketServices, ct) =>
            //        Task.FromResult(Observable.Return(websocketServices)))
            //    .Select(websocketService => Observable.FromAsync(ct => ConnectWebsocket(websocketService, ct)).Concat())
            //    .Concat();

            //return observableListener;

            async Task<IObservable<IDatagram>> ConnectWebsocket(WebsocketService ws, CancellationToken ct) =>
                await ws.WebsocketConnectHandler.ConnectWebsocket(
                    uri,
                    X509CertCollection,
                    TlsProtocolType,
                    InitializeSender,
                    _eventLoopScheduler,
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

        public virtual bool IsSecureConnectionScheme(Uri uri) => 
            uri.Scheme switch
            {
                "ws" => false,
                "http" => false,
                "wss" => true,
                "https" => true,
                _ => throw new ArgumentException("Unknown Uri type.")
            };

        public virtual bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (IgnoreServerCertificateErrors) return true;

            switch (sslPolicyErrors)
            {
                case SslPolicyErrors.RemoteCertificateNameMismatch:
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors}");
                case SslPolicyErrors.RemoteCertificateNotAvailable:
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNotAvailable}");
                case SslPolicyErrors.RemoteCertificateChainErrors:
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors}");
                case SslPolicyErrors.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sslPolicyErrors), sslPolicyErrors, null);
            }
            return true;
        }

        public void Dispose()
        {
            //_connectionStatusSubject.Dispose();
        }
    }
}
