using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL
{
    public class MessageWebSocketRx : IMessageWebSocketRx
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly IObserver<string> _observerMessage;

        private readonly WebSocketConnectService _webSocketConnectService;
        private readonly WebsocketListener _websocketListener;
        private readonly WebsocketSenderService _websocketSenderService;
        private CancellationTokenSource _innerCancellationTokenSource;

        private TcpClient _tcpClient;
        private Stream _tcpStream;
        
        private IDisposable _disposableMessage;

        public bool IsConnected => _websocketListener.IsConnected;
        public bool SubprotocolAccepted => _websocketListener.SubprotocolAccepted;

        public string SubprotocolAcceptedName => _websocketListener.SubprotocolAcceptedName;
        public string Origin { get; set; }
        public IDictionary<string, string> Headers { get; set; }
        public IEnumerable<string> Subprotocols { get; set; }
        public SslProtocols TlsProtocolType { get; set; }
        public X509CertificateCollection X509CertCollection { get; set; }
        public bool ExcludeZeroApplicationDataInPong { get; set; }
        public bool IgnoreServerCertificateErrors { get; set; }

        public IObservable<ConnectionStatus> ConnectionStatusObservable { get; }
        public IObservable<string> MessageReceiverObservable { get; }
        
        public MessageWebSocketRx()
        {
            
            var subjectMessageReceiver = new Subject<string>();

            MessageReceiverObservable = subjectMessageReceiver.AsObservable();
            _observerMessage = subjectMessageReceiver.AsObserver();

            var subjectConnectionStatus = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);

            ConnectionStatusObservable = subjectConnectionStatus.AsObservable();
            _observerConnectionStatus = subjectConnectionStatus.AsObserver();
            

            _webSocketConnectService = new WebSocketConnectService(_observerConnectionStatus);
            
            _websocketListener = new WebsocketListener(_webSocketConnectService, _observerConnectionStatus);
            _websocketSenderService = new WebsocketSenderService(_websocketListener);
        }

       
        public async Task ConnectAsync(
            Uri uri,
            CancellationToken token = default (CancellationToken))
        {
            _websocketListener.ExcludeZeroApplicationDataInPong = ExcludeZeroApplicationDataInPong;

            _observerConnectionStatus.OnNext(ConnectionStatus.Connecting);

            _innerCancellationTokenSource = new CancellationTokenSource();

            if (token != default(CancellationToken))
            {
                token.Register(() =>
                {
                    _innerCancellationTokenSource.Cancel();
                });
            }
            
            await ConnectTcpClient(uri, _innerCancellationTokenSource.Token);

            _tcpStream = await DetermineStreamTypeAsync(uri, _tcpClient, X509CertCollection, TlsProtocolType);

            await _webSocketConnectService.ConnectServer(
                uri,
                IsSecureWebsocket(uri),
                token,
                _tcpStream,
                Origin,
                Headers,
                Subprotocols);

            _disposableMessage = _websocketListener.CreateObservableListener(
                    _innerCancellationTokenSource,
                    _tcpStream)
                .Subscribe(
                    str => _observerMessage.OnNext(str),
                    ex =>
                    {
                        WaitForServerToCloseConnectionAsync().Wait(token);
                        _observerMessage.OnError(ex);
                    },
                    () =>
                    {
                        WaitForServerToCloseConnectionAsync().Wait(token);
                        _observerMessage.OnCompleted();
                    });
        }

        public async Task DisconnectAsync()
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Disconnecting);
            var dataReceiveDone = WaitForDataAsync();
            var timeoutDataTask = Task.Delay(TimeSpan.FromSeconds(1));

            // Wait for data or timeout after 1 second
            var dataReceivedDone = await Task.WhenAny(dataReceiveDone, timeoutDataTask);

            if (_tcpStream.CanRead)
            {
                await _websocketSenderService.SendCloseHandshakeAsync(_webSocketConnectService.TcpStream, StatusCodes.GoingAway);
            }

            var serverDisconnect = WaitForServerToCloseConnectionAsync();
            var timeoutServerCloseTask = Task.Delay(TimeSpan.FromSeconds(5));

            // Wait for server to close the connection or force a close.
            var disconnectResult = await Task.WhenAny(serverDisconnect, timeoutServerCloseTask);

            if (disconnectResult != serverDisconnect)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.ForcefullyDisconnected);
                _websocketListener.StopReceivingData();
            }
            else
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Disconnected);
            }

            _disposableMessage?.Dispose();
            _tcpStream?.Dispose();
            //_websocketListener.IsConnected = false;
        }

        public async Task SendTextAsync(string message)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Sending);
            await _websocketSenderService.SendTextAsync(_webSocketConnectService.TcpStream, message);
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
                    default:
                        break;
                }
                
                await _websocketSenderService.SendTextMultiFrameAsync(_webSocketConnectService.TcpStream, message, frameType);
            _observerConnectionStatus.OnNext(ConnectionStatus.FrameDeliveryAcknowledged);
        }

        public async Task SendTextAsync(string[] messageList)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Sending);
            await _websocketSenderService.SendTextAsync(_webSocketConnectService.TcpStream, messageList);
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
                catch (System.Exception ex)
                {
                    throw ex;
                }
            }
            else
            {
                return tcpClient.GetStream();
            }
        }

        private async Task ConnectTcpClient(Uri uri, CancellationToken ct)
        {
            _tcpClient?.Dispose();

            _tcpClient = new TcpClient();

            var connectTask = _tcpClient.ConnectAsync(uri.Host, uri.Port);
            var timeOut = Task.Delay(TimeSpan.FromSeconds(10), ct);

            try
            {
                var resultTask = await Task.WhenAny(connectTask, timeOut);

                if (resultTask != connectTask)
                {
                    throw new WebsocketClientLiteTcpConnectException($"TCP Socket connection timed-out to {uri.Host}:{uri.Port}.");
                }

            }
            catch (ObjectDisposedException)
            {
                // OK to ignore
            }
            catch (System.Exception ex)
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

        private async Task WaitForServerToCloseConnectionAsync()
        {
            _websocketListener.IsConnected = false;
            while (!_websocketListener.HasReceivedCloseFromServer)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            //while (_tcpStream.CanRead)
            //{
            //    await Task.Delay(TimeSpan.FromMilliseconds(50));
            //}
        }

        private async Task WaitForDataAsync()
        {
            while (!_websocketListener.TextDataParser.IsCloseRecieved)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
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
                    throw new System.Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors.ToString()}");
                case SslPolicyErrors.RemoteCertificateNotAvailable:
                    throw new System.Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNotAvailable.ToString()}");
                case SslPolicyErrors.RemoteCertificateChainErrors:
                    throw new System.Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors.ToString()}");
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
            _websocketListener?.StopReceivingData();
            _disposableMessage?.Dispose();
        }
    }
}
