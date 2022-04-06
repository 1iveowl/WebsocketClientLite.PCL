using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Extension;
using WebsocketClientLite.PCL.Factory;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL
{
    public class MessageWebSocketRx : IMessageWebSocketRx
    {
        private readonly BehaviorSubject<ConnectionStatus> _observerConnectionStatus;
        //private readonly IObserver<string> _observerMessage;
        //private readonly IObservable<ConnectionStatus> _connectionStatusObservable;
        //private readonly IObservable<string> _messageReceiverObservable;

        //private readonly WebsocketConnectHandler _webSocketConnectService;
        //private readonly WebsocketParserHandler _websocketParserHandler;
        //private readonly WebsocketSenderHandler _websocketSenderHandler;

        internal TcpClient TcpClient { get; private set; }
        // private WebsocketServices _websocketServices;

        private ISender _sender;

        public bool IsConnected { get; private set; }

        public bool SubprotocolAccepted { get; set; }

        //public IEnumerable<string> SubprotocolAcceptedNames { get; set; }

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

        //[Obsolete("Use ConnectObservable instead.")]
        public IObservable<ConnectionStatus> ConnectionStatusObservable { get; private set; } 

        //[Obsolete("Use ConnectObservable instead.")]
        //public IObservable<string> MessageReceiverObservable => _messageReceiverObservable;
                
        public MessageWebSocketRx(TcpClient tcpClient) 
        {
            TcpClient = tcpClient;
            Subprotocols = null;

            _observerConnectionStatus = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);
            ConnectionStatusObservable = _observerConnectionStatus.AsObservable();
        }

        public MessageWebSocketRx() : this(null)
        {
            
        }

        //public MessageWebSocketRx()
        //{

        //    //var subjectMessageReceiver = new Subject<string>();

        //    //_messageReceiverObservable = subjectMessageReceiver.AsObservable();
        //    //_observerMessage = subjectMessageReceiver.AsObserver();

        //    //var subjectConnectionStatus = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);

        //    //_connectionStatusObservable = subjectConnectionStatus.AsObservable();
        //    //_observerConnectionStatus = subjectConnectionStatus.AsObserver();

        //    //_webSocketConnectService = new WebSocketConnectHandler(_observerConnectionStatus, _observerMessage);

        //    //_websocketParserHandler = new WebsocketParserHandler(
        //    //    _observerConnectionStatus, 
        //    //    SubprotocolAccepted, 
        //    //    ExcludeZeroApplicationDataInPong);
        //}

        public IObservable<string> WebsocketConnectObservable (Uri uri, TimeSpan timeout = default)
        {
            WebsocketService websocket = null;



            var observableListener = Observable.Using(
                resourceFactoryAsync: ct => WebsocketService.Create(
                     () => IsSecureConnectionScheme(uri),
                     ValidateServerCertificate,
                     _observerConnectionStatus.AsObserver(),
                     this),
                observableFactoryAsync: (websocketServices, ct) =>
                    Task.FromResult(Observable.Return(websocketServices)))
                //.Do(websocketService => _sender = websocketService.WebsocketC)
                .Select(websocketService => Observable.FromAsync(_ => ConnectWebsocket(websocketService)).Concat())
                .Concat();

                //.SelectMany(websocketService => websocketService.WebsocketConnectHandler.ConnectWebsocket(
                //                    uri,
                //                    X509CertCollection,
                //                    TlsProtocolType,
                //                    timeout,
                //                    Subprotocols),
                //            (websocketService, tuple) => websocketService.WebsocketConnectHandler
                //                .SendHandShake(uri, tuple.sender, tuple.messageObservable, Origin, Headers))
                //.SelectMany(x => x)
                //.SelectMany(x => x);

            //.Select(websocketService => Observable
            //    .FromAsync(_ => websocketService.WebsocketConnectHandler.ConnectWebsocket(
            //                    uri,
            //                    X509CertCollection,
            //                    TlsProtocolType,
            //                    timeout,
            //                    Subprotocols)))
            //    .Concat()
            //.Select(tuple => )

            //.SelectMany(
            //    tuple => tuple.connectionHandler.SendHandShake(uri, tuple.sender, Origin, Headers),
            //    (tuple, sender) => tuple.connectionHandler.WaitForHandShake();

            return observableListener;

            async Task<IObservable<string>> ConnectWebsocket(WebsocketService ws) =>
                await ws.WebsocketConnectHandler.ConnectWebsocket(
                                    uri,
                                    X509CertCollection,
                                    TlsProtocolType,
                                    sender =>
                                    {
                                        _sender = sender;
                                        IsConnected = true;
                                    },
                                    timeout,
                                    Origin,
                                    Headers,
                                    Subprotocols);


            //return new WebsocketConnection()
            //{
            //    ConnectionStatusObservable = websocket.ConnectionStatusObservable,
            //    MessageObservable = observableListener,
            //    Sender = websocket.WebsocketConnectHandler.WebsocketSenderHandler
            //};

            //var observableListener = Observable.Using(
            //    resourceFactory: () => new TcpConnectionService(
            //        isSecureConnectionSchemeFunc: () => IsSecureConnectionScheme(uri),
            //        validateServerCertificate: ValidateServerCertificate),
            //    observableFactory: tcpConnectionService =>
            //        Observable.Return(tcpConnectionService)
            //            .Select(tc => Observable.FromAsync(_ => tc.Connect(
            //                    uri,
            //                    () => _observerConnectionStatus.OnNext(ConnectionStatus.TcpSocketConnected),
            //                    X509CertCollection,
            //                    TlsProtocolType,
            //                    timeout)))
            //            .Concat()
            //            .Do(tcpStream => sender = new WebsocketSenderHandler(_observerConnectionStatus, tcpStream))
            //            .Select(tcpStream => _websocketParserHandler
            //                .CreateWebsocketListenerObservable(
            //                    tcpStream: tcpStream,
            //                    connectToWebsocketFunc: () => _webSocketConnectService.ConnectToWebSocketServer(
            //                            _websocketParserHandler,
            //                            _websocketSenderHandler,
            //                            uri,
            //                            //tcpStream,
            //                            Origin,
            //                            Headers,
            //                            Subprotocols),
            //                    disconnectFunc: () => _webSocketConnectService.DisconnectWebsocketServer())))
            //            .SelectMany(x => x);

            //var t = _webSocketConnectService.ConnectToWebSocketServer(
            //    _websocketParserHandler,
            //    _websocketSenderHandler,
            //    uri,
            //    _tcpStream,
            //    Origin,
            //    Headers,
            //    Subprotocols);


        }


        //[Obsolete("Use ConnectObservable instead.")]
        //public async Task ConnectAsync(
        //    Uri uri,
        //    TimeSpan timeout = default)
        //{
        //    _observerConnectionStatus.OnNext(ConnectionStatus.ConnectingToTcpSocket);
            
        //    await ConnectTcpClient(uri, timeout);

        //    _tcpStream = await DetermineAndCreateStreamTypeAsync(uri, _tcpClient, X509CertCollection, TlsProtocolType);

        //    var websocketListenerObservable = _websocketParserHandler
        //        .CreateWebsocketListenerObservable(_tcpStream, () => _webSocketConnectService.DisconnectWebsocketServer());

        //    _disposableWebsocketListener = websocketListenerObservable
        //        //_disposableWebsocketListener = websocketListenerObservable
        //        // https://stackoverflow.com/a/45217578/4140832
        //        .FinallyAsync(async () =>
        //        {
        //            await _webSocketConnectService.DisconnectWebsocketServer();
        //        })
        //        .Subscribe(
        //            str => _observerMessage.OnNext(str),
        //            ex =>
        //            {
        //                _observerMessage.OnError(ex);
        //            },
        //            () =>
        //            {
        //                _observerMessage.OnCompleted();
        //            });

        //    await _webSocketConnectService.ConnectToWebSocketServer(
        //        _websocketParserHandler,
        //        _websocketSenderHandler,
        //        uri,
        //        _tcpStream,
        //        Origin,
        //        Headers,
        //        Subprotocols);
        //}

        //[Obsolete("Use ConnectObservable instead.")]
        //public async Task DisconnectAsync()
        //{
        //    await _webSocketConnectService.DisconnectWebsocketServer();

        //    _tcpClient?.Dispose();
        //    _disposableWebsocketListener?.Dispose();
        //}

        //public async Task SendTextAsync(string message)
        //{
        //    _observerConnectionStatus.OnNext(ConnectionStatus.Sending);

        //    await _websocketSenderHandler.SendTextAsync(_webSocketConnectService.TcpStream, message);

        //    _observerConnectionStatus.OnNext(ConnectionStatus.DeliveryAcknowledged);
        //}

        //public async Task SendTextMultiFrameAsync(string message, FrameType frameType)
        //{
        //    switch (frameType)
        //        {
        //            case FrameType.FirstOfMultipleFrames:
        //                _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingBegin);
        //                break;
        //            case FrameType.Continuation:
        //                _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingContinue);
        //                break;
        //            case FrameType.LastInMultipleFrames:
        //                _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingLast);
        //                break;
        //            case FrameType.CloseControlFrame:
        //                break;
        //            case FrameType.Single:
        //                break;
        //            default:
        //                throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
        //        }
                
        //    await _websocketSenderHandler.SendTextAsync(_webSocketConnectService.TcpStream, message, frameType);

        //    _observerConnectionStatus.OnNext(ConnectionStatus.FrameDeliveryAcknowledged);
        //}

        //public async Task SendTextAsync(string[] messageList)
        //{
        //    _observerConnectionStatus.OnNext(ConnectionStatus.Sending);

        //    await _websocketSenderHandler.SendTextAsync(_webSocketConnectService.TcpStream, messageList);

        //    _observerConnectionStatus.OnNext(ConnectionStatus.DeliveryAcknowledged);
        //}

        //private async Task<Stream> GetConnectedTcpStream(
        //    Uri uri,
        //    X509CertificateCollection x509CertificateCollection,
        //    SslProtocols tlsProtocolType,
        //    TimeSpan timeout = default)
        //{
        //    await ConnectAsync(uri, timeout);

        //    return await DetermineAndCreateStreamTypeAsync(uri, _tcpClient, x509CertificateCollection, SslProtocols.Tls);
        //}

        //private async Task<Stream> DetermineAndCreateStreamTypeAsync(
        //    Uri uri, 
        //    TcpClient tcpClient, 
        //    X509CertificateCollection x509CertificateCollection, 
        //    SslProtocols tlsProtocolType)
        //{

        //    if (IsSecureConnectionScheme(uri))
        //    {
        //        var secureStream = new SslStream(tcpClient.GetStream(), true, ValidateServerCertificate);

        //        try
        //        {
        //            await secureStream.AuthenticateAsClientAsync(uri.Host, x509CertificateCollection, tlsProtocolType, false);

        //            return secureStream;
        //        }
        //        catch (Exception ex)
        //        {
        //            throw new WebsocketClientLiteException("Unable to determine stream type", ex);
        //        }
        //    }

        //    return tcpClient.GetStream();
        //}

        public virtual bool IsSecureConnectionScheme(Uri uri) => 
            uri.Scheme switch
            {
                "ws" => false,
                "http" => false,
                "wss" => true,
                "https" => true,
                _ => throw new ArgumentException("Unknown Uri type.")
            };

        //private async Task ConnectTcpClient(Uri uri, TimeSpan timeout = default)
        //{
        //    if (!_isTcpClientProvided)
        //    {
        //        _tcpClient?.Dispose();
        //        _tcpClient = new TcpClient(
        //            uri.HostNameType == UriHostNameType.IPv6 
        //                ? AddressFamily.InterNetworkV6 
        //                : AddressFamily.InterNetwork);
        //    }
        //    else
        //    {
        //        if (_tcpClient is null)
        //        {
        //            throw new WebsocketClientLiteException($"When using the 'MessageWebSocketRx(TcpClient tcpClient)' constructor a valid TcpClient must be provided.");
        //        }
        //    }

        //    try
        //    {
        //        await _tcpClient
        //            .ConnectAsync(uri.Host, uri.Port)
        //            .ToObservable().Timeout(timeout != default ? timeout : TimeSpan.FromSeconds(5));
        //    }
        //    catch (TimeoutException ex)
        //    {
        //        throw new WebsocketClientLiteTcpConnectException($"TCP Socket connection timed-out to {uri.Host}:{uri.Port}.", ex);
        //    }
        //    catch (ObjectDisposedException)
        //    {
        //        // OK to ignore
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new WebsocketClientLiteTcpConnectException($"Unable to establish TCP Socket connection to: {uri.Host}:{uri.Port}.", ex);
        //    }

        //    if (_tcpClient.Connected)
        //    {
        //        _observerConnectionStatus.OnNext(ConnectionStatus.TcpSocketConnected);
        //        Debug.WriteLine("Connected");
        //    }
        //    else
        //    {
        //        throw new WebsocketClientLiteTcpConnectException($"Unable to connect to Tcp socket for: {uri.Host}:{uri.Port}.");
        //    }
        //}

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
            //_tcpStream?.Dispose();
            //_websocketParserHandler?.Dispose();
            //_disposableWebsocketListener?.Dispose();

            _observerConnectionStatus.Dispose();
        }
    }
}
