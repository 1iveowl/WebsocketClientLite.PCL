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
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL
{
    public class MessageWebSocketRxOld : IMessageWebSocketRx
    {
        public IObservable<ConnectionStatus> ConnectionStatusObservable { get; }

        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly WebSocketConnectService _webSocketConnectService;
        private readonly WebsocketListener _websocketListener;
        private readonly WebsocketSenderService _websocketSenderService;
        private CancellationTokenSource _innerCancellationTokenSource;

        private Stream _tcpStream;

        private bool _ignoreCertificateErrors;

        public bool IsConnected => _websocketListener.IsConnected;
        public bool SubprotocolAccepted => _websocketListener.SubprotocolAccepted;

        public string SubprotocolAcceptedName => _websocketListener.SubprotocolAcceptedName;

        public MessageWebSocketRxOld()
        {
            var subjectConnectionStatus = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);

            ConnectionStatusObservable = subjectConnectionStatus.AsObservable();
            _observerConnectionStatus = subjectConnectionStatus.AsObserver();

            _webSocketConnectService = new WebSocketConnectService(_observerConnectionStatus);

            _websocketListener = new WebsocketListener(_observerConnectionStatus);
            _websocketSenderService = new WebsocketSenderService(_websocketListener);
        }

        public IObservable<string> CreateObservableMessageReceiver(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subProtocols = null,
            bool ignoreServerCertificateErrors = false,
            SslProtocols tlsProtocolType = SslProtocols.Tls12,
            bool excludeZeroApplicationDataInPong = false,
            X509CertificateCollection x509CertificateCollection = null,
            CancellationToken token = default)
        {
            _websocketListener.ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;
            _observerConnectionStatus.OnNext(ConnectionStatus.Connecting);

            _innerCancellationTokenSource = new CancellationTokenSource();

            _ignoreCertificateErrors = ignoreServerCertificateErrors;

            if (token != default)
            {
                token.Register(() =>
                {
                    _innerCancellationTokenSource.Cancel();
                });
            }

            var tcpClient = new TcpClient();

            var connectTask = tcpClient.ConnectAsync(uri.Host, uri.Port);
            var timeOut = Task.Delay(TimeSpan.FromSeconds(10), token);

            try
            {
                var resultTask = await Task.WhenAny(connectTask, timeOut);

                if (resultTask == connectTask)
                {

                }
                else
                {

                }
            }
            catch (ObjectDisposedException)
            {
                // OK to ignore
            }
            catch (Exception ex)
            {
                throw ex;
            }

            if (tcpClient.Connected)
            {
                Debug.WriteLine("Connected");
            }
            else
            {
                throw new SocketException();
                Debug.WriteLine("Unable to connect");
            }

            if (IsSecureWebsocket(uri))
            {
                var secureStream = new SslStream(tcpClient.GetStream(), true, ValidateServerCertificate);

                try
                {
                    await secureStream.AuthenticateAsClientAsync(uri.Host, x509CertificateCollection, tlsProtocolType, false);

                    _tcpStream = secureStream;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            else
            {
                _tcpStream = tcpClient.GetStream();
            }

            if (!tcpClient.Connected)
            {
                throw new Exception($"Websocket Lite unable to connect to host: {uri.Host}");
            }

             await _webSocketConnectService.ConnectWebsocketServerAsync(
                uri,
                IsSecureWebsocket(uri),
                token,
                _tcpStream,
                origin,
                headers,
                subProtocols);

            var observableWebsocket = Observable.Create<string>(
                obs =>
                {
                    var disposableWebsocketListener = _websocketListener.CreateObservableListener(
                            _innerCancellationTokenSource,
                            _tcpStream)
                        .Subscribe(
                            str => obs.OnNext(str),
                            ex =>
                            {
                                WaitForServerToCloseConnectionAsync().Wait(token);
                                obs.OnError(ex);
                            },
                            () =>
                            {
                                WaitForServerToCloseConnectionAsync().Wait(token);
                                obs.OnCompleted();
                            });

                    return disposableWebsocketListener;
                });

            return observableWebsocket;
        }

        public async Task CloseAsync()
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Disconnecting);
            var dataReceiveDone = WaitForDataAsync();
            var timeoutDataTask = Task.Delay(TimeSpan.FromSeconds(1));

            // Wait for data or timeout after 1 second
            var dataReceivedDone = await Task.WhenAny(dataReceiveDone, timeoutDataTask);

            if (_tcpStream.CanRead)
            {
                await _websocketSenderService.SendCloseHandshakeAsync(_tcpStream, StatusCodes.GoingAway);
            }

            var serverDisconnect = WaitForServerToCloseConnectionAsync();
            var timeoutServerCloseTask = Task.Delay(TimeSpan.FromSeconds(2));

            // Wait for server to close the connection or force a close.
            var disconnectResult = await Task.WhenAny(serverDisconnect, timeoutServerCloseTask);
            if (disconnectResult != serverDisconnect)
            {
                _websocketListener.StopReceivingData();
            }

            _observerConnectionStatus.OnNext(ConnectionStatus.Disconnected);
            //_websocketListener.IsConnected = false;
        }

        public async Task SendTextAsync(string message)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Sending);
            await _websocketSenderService.SendTextAsync(_tcpStream, message);
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

            await _websocketSenderService.SendTextMultiFrameAsync(_tcpStream, message, frameType);
            _observerConnectionStatus.OnNext(ConnectionStatus.FrameDeliveryAcknowledged);
        }

        public async Task SendTextAsync(string[] messageList)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Sending);
            await _websocketSenderService.SendTextAsync(_tcpStream, messageList);
            _observerConnectionStatus.OnNext(ConnectionStatus.DeliveryAcknowledged);
        }

        private async Task WaitForServerToCloseConnectionAsync()
        {
            _websocketListener.IsConnected = false;
            while (!_websocketListener.HasReceivedCloseFromServer)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            while (_tcpStream.CanRead)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }

        private async Task WaitForDataAsync()
        {
            while (!_websocketListener.TextDataParser.IsCloseRecieved)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }

        private bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (_ignoreCertificateErrors) return true;

            switch (sslPolicyErrors)
            {
                case SslPolicyErrors.RemoteCertificateNameMismatch:
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors.ToString()}");
                case SslPolicyErrors.RemoteCertificateNotAvailable:
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNotAvailable.ToString()}");
                case SslPolicyErrors.RemoteCertificateChainErrors:
                    throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors.ToString()}");
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
            _tcpStream.Dispose();
            _websocketListener.StopReceivingData();
            _innerCancellationTokenSource.Dispose();
        }
    }
}
