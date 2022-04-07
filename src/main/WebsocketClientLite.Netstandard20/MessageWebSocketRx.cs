using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Factory;

namespace WebsocketClientLite.PCL
{
    public class MessageWebSocketRx : IMessageWebSocketRx
    {
        private readonly BehaviorSubject<ConnectionStatus> _observerConnectionStatus;
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

        public IObservable<ConnectionStatus> ConnectionStatusObservable { get; private set; } 
                
        public MessageWebSocketRx(TcpClient tcpClient) 
        {
            _eventLoopScheduler = new EventLoopScheduler();

            TcpClient = tcpClient;
            Subprotocols = null;

            _observerConnectionStatus = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);
            ConnectionStatusObservable = _observerConnectionStatus
                .AsObservable()
                .ObserveOn(_eventLoopScheduler);
        }

        public MessageWebSocketRx() : this(null)
        {
            
        }

        public IObservable<string> WebsocketConnectObservable (Uri uri, TimeSpan timeout = default)
        {
            var observableListener = Observable.Using(
                resourceFactoryAsync: ct => WebsocketServiceFactory.Create(
                     () => IsSecureConnectionScheme(uri),
                     ValidateServerCertificate,
                     _observerConnectionStatus.AsObserver(),
                     this),
                observableFactoryAsync: (websocketServices, ct) =>
                    Task.FromResult(Observable.Return(websocketServices)))
                .Select(websocketService => Observable.FromAsync(_ => ConnectWebsocket(websocketService)).Concat())
                .Concat()
                .ObserveOn(_eventLoopScheduler);

            return observableListener;

            async Task<IObservable<string>> ConnectWebsocket(WebsocketServiceFactory ws) =>
                await ws.WebsocketConnectHandler.ConnectWebsocket(
                                    uri,
                                    X509CertCollection,
                                    TlsProtocolType,
                                    InitializeSender,
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
            _observerConnectionStatus.Dispose();
        }
    }
}
