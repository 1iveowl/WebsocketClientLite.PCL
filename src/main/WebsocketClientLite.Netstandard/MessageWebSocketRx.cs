using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using HttpMachine;
using ISocketLite.PCL.Interface;
using ISocketLite.PCL.Model;
using IWebsocketClientLite.PCL;
using SocketLite.Services;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL
{
    public class MessageWebSocketRx : IMessageWebSocketRx
    {

        #region Obsolete
        [Obsolete("Deprecated")]
        public IObservable<string> ObserveTextMessagesReceived => _websocketListener.ObserveTextMessageSequence;

        [Obsolete("Deprecated")]
        public async Task ConnectAsync(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolVersion = TlsProtocolVersion.Tls12,
            bool excludeZeroApplicationDataInPong = false)
        {
            _websocketListener.ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;

            _connectionStatusObserver.OnNext(ConnectionStatus.Connecting);

            _innerCancellationTokenSource = new CancellationTokenSource();

            using (_httpParserDelegate = new HttpParserDelegate())
            using (_httpParserHandler = new HttpCombinedParser(_httpParserDelegate))
            {
                var isSecure = IsSecureWebsocket(uri);

                try
                {
                    await _webSocketConnectService.ConnectAsync(
                        uri,
                        isSecure,
                        _httpParserDelegate,
                        _httpParserHandler,
                        _innerCancellationTokenSource,
                        _websocketListener,
                        origin,
                        headers,
                        subprotocols,
                        ignoreServerCertificateErrors,
                        tlsProtocolVersion);
                }
                catch (Exception ex)
                {
                    _connectionStatusObserver.OnNext(ConnectionStatus.ConnectionFailed);
                    throw ex;
                }

                if (_httpParserDelegate.HttpRequestReponse.StatusCode == 101)
                {
                    if (subprotocols != null)
                    {
                        _websocketListener.SubprotocolAccepted = _httpParserDelegate?.HttpRequestReponse?.Headers?.ContainsKey("SEC-WEBSOCKET-PROTOCOL") ?? false;

                        if (SubprotocolAccepted)
                        {
                            _websocketListener.SubprotocolAcceptedName = _httpParserDelegate?.HttpRequestReponse?.Headers?["SEC-WEBSOCKET-PROTOCOL"];
                            if (!string.IsNullOrEmpty(SubprotocolAcceptedName))
                            {
                                _connectionStatusObserver.OnNext(ConnectionStatus.Connected);
                                _websocketListener.IsConnected = true;
                            }
                            else
                            {
                                throw new Exception("Server responded with blank Sub Protocol name");
                            }
                        }
                        else
                        {
                            throw new Exception("Server did not support any of the needed Sub Protocols");
                        }
                    }
                    else
                    {
                        _connectionStatusObserver.OnNext(ConnectionStatus.Connected);
                        _websocketListener.IsConnected = true;
                    }
                    _websocketListener.DataReceiveMode = DataReceiveMode.IsListeningForTextData;
                }
            }
        }

        [Obsolete("Deprecated")]
        public async Task CloseAsync()
        {
            _connectionStatusObserver.OnNext(ConnectionStatus.Disconnecting);
            var dataReceiveDone = WaitForDataAsync();
            var timeoutDataTask = Task.Delay(TimeSpan.FromSeconds(1));

            // Wait for data or timeout after 1 second
            var dataReceivedDone = await Task.WhenAny(dataReceiveDone, timeoutDataTask);

            if (_tcpSocketClient.IsConnected)
            {
                await _websocketSenderService.SendCloseHandshakeAsync(_webSocketConnectService.TcpSocketClient, StatusCodes.GoingAway);
            }

            var serverDisconnect = WaitForServerToCloseConnectionAsync();
            var timeoutServerCloseTask = Task.Delay(TimeSpan.FromSeconds(2));

            // Wait for server to close the connection or force a close.
            var disconnectresult = await Task.WhenAny(serverDisconnect, timeoutServerCloseTask);
            if (disconnectresult != serverDisconnect)
            {
                _websocketListener.StopReceivingData();
            }

            _connectionStatusObserver.OnNext(ConnectionStatus.Disconnected);
            _websocketListener.IsConnected = false;
        }

        #endregion

        //internal ITcpSocketClient TcpSocketClient;

        public IObservable<ConnectionStatus> ObserveConnectionStatus => _connectionStatusObserver.AsObservable();

        private readonly ITcpSocketClient _tcpSocketClient = new TcpSocketClient();
        private readonly ISubject<ConnectionStatus> _connectionStatusObserver = new Subject<ConnectionStatus>();
        private readonly WebSocketConnectService _webSocketConnectService;
        private readonly WebsocketListener _websocketListener;
        private readonly WebsocketSenderService _websocketSenderService;

        private HttpParserDelegate _httpParserDelegate;
        private HttpCombinedParser _httpParserHandler;

        private CancellationTokenSource _innerCancellationTokenSource;


        public bool IsConnected => _websocketListener.IsConnected;
        public bool SubprotocolAccepted => _websocketListener.SubprotocolAccepted;

        public string SubprotocolAcceptedName => _websocketListener.SubprotocolAcceptedName;
        
        public MessageWebSocketRx()
        {
            _webSocketConnectService = new WebSocketConnectService();
            
            _websocketListener = new WebsocketListener(_webSocketConnectService);
            _websocketSenderService = new WebsocketSenderService(_websocketListener);
        }
        
        public async Task<IObservable<string>> CreateObservableMessageReceiver(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subProtocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12,
            bool excludeZeroApplicationDataInPong = false)
        {
            _websocketListener.ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;
            _connectionStatusObserver.OnNext(ConnectionStatus.Connecting);
            _innerCancellationTokenSource = new CancellationTokenSource();

            ITcpSocketClient socketClient = new TcpSocketClient();

            _httpParserDelegate = new HttpParserDelegate();
            _httpParserHandler = new HttpCombinedParser(_httpParserDelegate);

            var observableSocketListener = _websocketListener.CreateObservableListener(
                _httpParserDelegate,
                _httpParserHandler,
                _innerCancellationTokenSource,
                socketClient);

            await socketClient.ConnectAsync(
                uri.Host,
                uri.Port.ToString(),
                IsSecureWebsocket(uri),
                _innerCancellationTokenSource.Token,
                ignoreServerCertificateErrors,
                tlsProtocolType);

            
            await _webSocketConnectService.ConnectServer(
                observableSocketListener,
                uri,
                IsSecureWebsocket(uri),
                _httpParserDelegate,
                _httpParserHandler,
                _innerCancellationTokenSource,
                _websocketListener,
                socketClient,
                origin,
                headers,
                subProtocols,
                ignoreServerCertificateErrors,
                tlsProtocolType);

            return observableSocketListener;
        }

        public async Task SendTextAsync(string message)
        {
            _connectionStatusObserver.OnNext(ConnectionStatus.Sending);
            await _websocketSenderService.SendTextAsync(_webSocketConnectService.TcpSocketClient, message);
            _connectionStatusObserver.OnNext(ConnectionStatus.DeliveryAcknowledged);

        }

        public async Task SendTextMultiFrameAsync(string message, FrameType frameType)
        {
            switch (frameType)
                {
                    case FrameType.FirstOfMultipleFrames:
                        _connectionStatusObserver.OnNext(ConnectionStatus.MultiFrameSendingBegin);
                        break;
                    case FrameType.Continuation:
                        _connectionStatusObserver.OnNext(ConnectionStatus.MultiFrameSendingContinue);
                        break;
                    case FrameType.LastInMultipleFrames:
                        _connectionStatusObserver.OnNext(ConnectionStatus.MultiFrameSendingLast);
                        break;
                    case FrameType.CloseControlFrame:
                        break;
                    default:
                        break;
                }
                
                await _websocketSenderService.SendTextMultiFrameAsync(_webSocketConnectService.TcpSocketClient, message, frameType);
                _connectionStatusObserver.OnNext(ConnectionStatus.FrameDeliveryAcknowledged);
        }

        public async Task SendTextAsync(string[] messageList)
        {
            _connectionStatusObserver.OnNext(ConnectionStatus.Sending);
            await _websocketSenderService.SendTextAsync(_webSocketConnectService.TcpSocketClient, messageList);
            _connectionStatusObserver.OnNext(ConnectionStatus.DeliveryAcknowledged);
        }


        private async Task WaitForServerToCloseConnectionAsync()
        {
            while (!_websocketListener.HasReceivedCloseFromServer)
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

        private bool IsSecureWebsocket(Uri uri)
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
            _tcpSocketClient.Dispose();
            _webSocketConnectService.Disconnect();
            _websocketListener.StopReceivingData();
        }

        private async Task WaitForWebsocketConnection()
        {
            while (!_websocketListener.IsConnected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
        }
    }
}
