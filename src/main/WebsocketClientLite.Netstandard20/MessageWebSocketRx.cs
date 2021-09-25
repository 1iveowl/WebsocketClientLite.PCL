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
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL
{
    public class MessageWebSocketRx : IMessageWebSocketRx
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly IObserver<string> _observerMessage;

        private readonly WebSocketConnectHandler _webSocketConnectService;
        private readonly WebsocketParserHandler _websocketParserHandler;
        private readonly WebsocketSenderHandler _websocketSenderHandler;

        private TcpClient _tcpClient;
        private Stream _tcpStream;
        
        private IDisposable _disposableWebsocketListener;

        public bool SubprotocolAccepted { get; set; }

        public string SubprotocolAcceptedName => _websocketParserHandler.SubprotocolAcceptedName;

        public string Origin { get; set; }

        public IDictionary<string, string> Headers { get; set; }

        public IEnumerable<string> Subprotocols { get; set; }

        public SslProtocols TlsProtocolType { get; set; }

        public X509CertificateCollection X509CertCollection { get; set; }

        public bool ExcludeZeroApplicationDataInPong { get; set; }

        public bool IgnoreServerCertificateErrors { get; set; }

        public IObservable<ConnectionStatus> ConnectionStatusObservable { get; }

        public IObservable<string> MessageReceiverObservable { get; }

        private readonly bool _isTcpClientProvided;

        public MessageWebSocketRx(TcpClient tcpClient)  : this()
        {
            _tcpClient = tcpClient;
            _isTcpClientProvided = true;
        }
        
        public MessageWebSocketRx()
        {
            
            var subjectMessageReceiver = new Subject<string>();

            MessageReceiverObservable = subjectMessageReceiver.AsObservable();
            _observerMessage = subjectMessageReceiver.AsObserver();

            var subjectConnectionStatus = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);

            ConnectionStatusObservable = subjectConnectionStatus.AsObservable();
            _observerConnectionStatus = subjectConnectionStatus.AsObserver();

            _webSocketConnectService = new WebSocketConnectHandler(_observerConnectionStatus, _observerMessage);
            
            _websocketParserHandler = new WebsocketParserHandler(
                _observerConnectionStatus, 
                SubprotocolAccepted, 
                ExcludeZeroApplicationDataInPong);

            _websocketSenderHandler = new WebsocketSenderHandler( _observerConnectionStatus);
        }

       
        public async Task ConnectAsync(
            Uri uri,
            TimeSpan timeout = default)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.ConnectingToTcpSocket);
            
            await ConnectTcpClient(uri, timeout);

            _tcpStream = await DetermineStreamTypeAsync(uri, _tcpClient, X509CertCollection, TlsProtocolType);

            var websocketListenerObservable = _websocketParserHandler.CreateWebsocketListenerObservable(_tcpStream);

            _disposableWebsocketListener = websocketListenerObservable
                // https://stackoverflow.com/a/45217578/4140832
                .FinallyAsync(async () =>
                {
                    await _webSocketConnectService.DisconnectWebsocketServer();
                })
                .Subscribe(
                    str => _observerMessage.OnNext(str),
                    ex =>
                    {
                        _observerMessage.OnError(ex);
                    },
                    () =>
                    {
                        _observerMessage.OnCompleted();
                    });

            await _webSocketConnectService.ConnectToWebSocketServer(
                _websocketParserHandler,
                _websocketSenderHandler,
                uri,
                IsSecureWebsocket(uri),
                _tcpStream,
                Origin,
                Headers,
                Subprotocols);
        }

        public async Task DisconnectAsync()
        {
            await _webSocketConnectService.DisconnectWebsocketServer();

            _tcpClient?.Dispose();
            _disposableWebsocketListener?.Dispose();
        }

        public async Task SendTextAsync(string message)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Sending);

            await _websocketSenderHandler.SendTextAsync(_webSocketConnectService.TcpStream, message);

            _observerConnectionStatus.OnNext(ConnectionStatus.DeliveryAcknowledged);
            }

        public async Task SendTextMultiFrameAsync(string message, FrameType frameType)
        {
            switch (frameType)
                {
                    case FrameType.FirstOfMultipleFrames:
                        _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingBegin);
                        break;
                    case FrameType.Continuation:
                        _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingContinue);
                        break;
                    case FrameType.LastInMultipleFrames:
                        _observerConnectionStatus.OnNext(ConnectionStatus.MultiFrameSendingLast);
                        break;
                    case FrameType.CloseControlFrame:
                        break;
                    case FrameType.Single:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
                }
                
            await _websocketSenderHandler.SendTextMultiFrameAsync(_webSocketConnectService.TcpStream, message, frameType);

            _observerConnectionStatus.OnNext(ConnectionStatus.FrameDeliveryAcknowledged);
        }

        public async Task SendTextAsync(string[] messageList)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Sending);

            await _websocketSenderHandler.SendTextAsync(_webSocketConnectService.TcpStream, messageList);

            _observerConnectionStatus.OnNext(ConnectionStatus.DeliveryAcknowledged);
        }

        private async Task<Stream> DetermineStreamTypeAsync(Uri uri, TcpClient tcpClient, X509CertificateCollection x509CertificateCollection, SslProtocols tlsProtocolType)
        {
            if (IsSecureWebsocket(uri))
            {
                var secureStream = new SslStream(tcpClient.GetStream(), true, ValidateServerCertificate);

                try
                {
                    await secureStream.AuthenticateAsClientAsync(uri.Host, x509CertificateCollection, tlsProtocolType, false);

                    return secureStream;
                }
                catch (Exception ex)
                {
                    throw new WebsocketClientLiteException("Unable to determine stream type", ex);
                }
            }

            return tcpClient.GetStream();
        }

        private async Task ConnectTcpClient(Uri uri, TimeSpan timeout = default)
        {
            if (!_isTcpClientProvided)
            {
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient(
                    uri.HostNameType == UriHostNameType.IPv6 
                        ? AddressFamily.InterNetworkV6 
                        : AddressFamily.InterNetwork);
            }
            else
            {
                if (_tcpClient is null)
                {
                    throw new WebsocketClientLiteException($"When using the 'MessageWebSocketRx(TcpClient tcpClient)' constructor a valid TcpClient must be provided.");
                }
            }

            try
            {
                await _tcpClient.ConnectAsync(uri.Host, uri.Port).ToObservable().Timeout(timeout != default ? timeout : TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException ex)
            {
                throw new WebsocketClientLiteTcpConnectException($"TCP Socket connection timed-out to {uri.Host}:{uri.Port}.", ex);
            }
            catch (ObjectDisposedException)
            {
                // OK to ignore
            }
            catch (Exception ex)
            {
                throw new WebsocketClientLiteTcpConnectException($"Unable to establish TCP Socket connection to: {uri.Host}:{uri.Port}.", ex);
            }

            if (_tcpClient.Connected)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.TcpSocketConnected);
                Debug.WriteLine("Connected");
            }
            else
            {
                throw new WebsocketClientLiteTcpConnectException($"Unable to connect to Tcp socket for: {uri.Host}:{uri.Port}.");
            }
        }

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

        private static bool IsSecureWebsocket(Uri uri)
        {
            bool secure;

            switch (uri.Scheme.ToLower())
            {
                case "ws":
                    {
                        secure = false;
                        break;
                    }
                case "wss":
                    {
                        secure = true;
                        break; ;
                    }
                default: throw new ArgumentException("Uri is not Websocket kind.");
            }
            return secure;
        }

        public void Dispose()
        {
            _tcpStream?.Dispose();
            _websocketParserHandler?.Dispose();
            _disposableWebsocketListener?.Dispose();

            if (!_isTcpClientProvided)
            {
                _tcpClient?.Dispose();
            }
        }
    }
}
