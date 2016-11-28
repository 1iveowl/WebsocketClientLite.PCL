using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HttpMachine;
using ISocketLite.PCL.Interface;
using ISocketLite.PCL.Model;
using IWebsocketClientLite.PCL;
using SocketLite.Services;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL
{
    public class MessageWebSocketRx : IMessageWebSocketRx
    {
        private readonly ITcpSocketClient _tcpSocketClient = new TcpSocketClient();
        private readonly WebSocketConnectService _webSocketConnectService;
        private readonly WebsocketListener _websocketListener;
        private readonly WebsocketSenderService _websocketSenderService;

        private HttpParserDelegate _httpParserDelegate;
        private HttpCombinedParser _httpParserHandler;
        private IDisposable _outerCancellationRegistration;

        private CancellationTokenSource _innerCancellationTokenSource;

        public IObservable<string> ObserveTextMessagesReceived => _websocketListener.ObserveTextMessageSequence;

        public bool IsConnected { get; private set; }
        public bool SubprotocolAccepted { get; private set; }

        public string SubprotocolAcceptedName { get; private set; }


        public MessageWebSocketRx()
        {
            _webSocketConnectService = new WebSocketConnectService();
            _websocketSenderService = new WebsocketSenderService();

            _websocketListener = new WebsocketListener(
                //_tcpSocketClient,
                _webSocketConnectService);
        }

        public async Task ConnectAsync(
            Uri uri, 
            CancellationTokenSource outerCancellationTokenSource,
            string origin = null,
            IEnumerable<string> subprotocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolVersion = TlsProtocolVersion.Tls12)
        {
            _outerCancellationRegistration = outerCancellationTokenSource.Token.Register(() =>
            {
                _innerCancellationTokenSource.Cancel();
            });

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
                    subprotocols,
                    ignoreServerCertificateErrors,
                    tlsProtocolVersion);
                }
                catch (Exception ex)
                {
                    throw ex;
                }

                if (_httpParserDelegate.HttpRequestReponse.StatusCode == 101)
                {
                    if (subprotocols != null)
                    {
                        SubprotocolAccepted = _httpParserDelegate?.HttpRequestReponse?.Headers?.ContainsKey("SEC-WEBSOCKET-PROTOCOL") ?? false;

                        if (SubprotocolAccepted)
                        {
                            SubprotocolAcceptedName = _httpParserDelegate?.HttpRequestReponse?.Headers?["SEC-WEBSOCKET-PROTOCOL"];
                            if (!string.IsNullOrEmpty(SubprotocolAcceptedName))
                            {
                                IsConnected = true;
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
                        IsConnected = true;
                    }
                    _websocketListener.DataReceiveMode = DataReceiveMode.IsListeningForTextData;
                }
            }
        }

        public async Task SendTextAsync(string message)
        {
            if (IsConnected)
            {
                await _websocketSenderService.SendTextAsync(_webSocketConnectService.TcpSocketClient, message);
            }
            else
            {
                IsConnected = false;
                throw new Exception("Not connected. Client must beconnected to websocket server before sending message");
            }
        }

        public async Task SendTextMultiFrameAsync(string message, FrameType frameType)
        {
            if (IsConnected)
            {
                await _websocketSenderService.SendTextMultiFrameAsync(_webSocketConnectService.TcpSocketClient, message, frameType);
            }
            else
            {
                IsConnected = false;
                throw new Exception("Not connected. Client must beconnected to websocket server before sending message");
            }
        }

        public async Task SendTextAsync(string[] messageList)
        {
            if (IsConnected)
            {
                await _websocketSenderService.SendTextAsync(_webSocketConnectService.TcpSocketClient, messageList);
            }
            else
            {
                IsConnected = false;
                throw new Exception("Not connected. Client must beconnected to websocket server before sending message");
            }
        }
        public async Task CloseAsync()
        {
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
                _websocketListener.Stop();
            }

            IsConnected = false;
            _outerCancellationRegistration.Dispose();
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

    }
}
