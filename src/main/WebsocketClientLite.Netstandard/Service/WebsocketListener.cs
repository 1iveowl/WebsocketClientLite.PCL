using HttpMachine;
using ISocketLite.PCL.Interface;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketListener
    {
        private readonly ISubject<string> _textMessageSequence = new Subject<string>();
        private readonly HandshakeParser _handshakeParser = new HandshakeParser();
        private readonly WebSocketConnectService _webSocketConnectService;
        internal readonly TextDataParser TextDataParser;

        private ITcpSocketClient _tcpSocketClient;
        private CancellationTokenSource _innerCancellationTokenSource;
        private HttpParserDelegate _parserDelgate;
        private HttpCombinedParser _parserHandler;
        private IDisposable _byteStreamSessionSubscription;
        internal bool ExcludeZeroApplicationDataInPong;

        internal bool IsConnected { get; set; }
        internal bool SubprotocolAccepted { get; set; }

        internal string SubprotocolAcceptedName { get; set; }

        [Obsolete("Deprecated")]
        private IObservable<string> ObserveTextMessageSession => _tcpSocketClient
            .ReadStream.ReadOneByteAtTheTimeObservable(_innerCancellationTokenSource)
            .Select(
            b =>
            {
                if (TextDataParser.IsCloseRecieved) return string.Empty;

                switch (DataReceiveMode)
                {
                    case DataReceiveMode.IsListeningForHandShake:
                        try
                        {
                            if (_parserDelgate.HttpRequestReponse.IsEndOfMessage)
                            {
                                return string.Empty;
                            }

                            _handshakeParser.Parse(b, _parserDelgate, _parserHandler);
                            return null;
                        }
                        catch (Exception ex)
                        {
                            if (ex is TimeoutException)
                            {
                                _parserDelgate.HttpRequestReponse.IsRequestTimedOut = true;
                            }
                            else
                            {
                                _parserDelgate.HttpRequestReponse.IsUnableToParseHttp = true;
                            }
                            return null;
                        }
                    case DataReceiveMode.IsListeningForTextData:

                        TextDataParser.Parse(_tcpSocketClient, b[0], ExcludeZeroApplicationDataInPong);

                        if (TextDataParser.IsCloseRecieved)
                        {
                            StopReceivingData();
                        }
                        return TextDataParser.HasNewMessage ? TextDataParser.NewMessage : null;

                    default:
                        return null;
                }
            }).Where(str => !string.IsNullOrEmpty(str));
        
        internal IObservable<string> ObserveTextMessageSequence => _textMessageSequence.AsObservable();

        internal DataReceiveMode DataReceiveMode { private get; set; } = DataReceiveMode.IsListeningForHandShake;

        internal bool HasReceivedCloseFromServer { get; private set; }

        internal WebsocketListener(WebSocketConnectService webSocketConnectService)
        {
            _webSocketConnectService = webSocketConnectService;
            TextDataParser = new TextDataParser();
        }

        internal void Start(
            HttpParserDelegate requestHandler,
            HttpCombinedParser parserHandler,
            CancellationTokenSource innerCancellationTokenSource)
        {
            _parserHandler = parserHandler;
            _parserDelgate = requestHandler;
            TextDataParser.Reinitialize();
            _innerCancellationTokenSource = innerCancellationTokenSource;

            _tcpSocketClient = _webSocketConnectService.TcpSocketClient;

            _byteStreamSessionSubscription = ObserveTextMessageSession.Subscribe(
                str =>
                {
                    _textMessageSequence.OnNext(str);
                },
                ex =>
                {
                    _textMessageSequence.OnError(ex);
                },
                () =>
                {
                    _textMessageSequence.OnCompleted();
                });

            HasReceivedCloseFromServer = false;
        }
        
        internal IObservable<string> CreateObservableListener(
            HttpParserDelegate parserDelegate,
            HttpCombinedParser parserHandler,
            CancellationTokenSource innerCancellationTokenSource,
            ITcpSocketClient tcpSocketClient,
            IEnumerable<string> subProtocols = null)
        {
            _parserHandler = parserHandler;
            _parserDelgate = parserDelegate;
            TextDataParser.Reinitialize();

            _innerCancellationTokenSource = innerCancellationTokenSource;

            _tcpSocketClient = tcpSocketClient;

            DataReceiveMode = DataReceiveMode.IsListeningForHandShake;
            IsConnected = false;

            var observable = Observable.Create<string>(
                obs =>
                {
                    var disp = _tcpSocketClient.ReadStream.ReadOneByteAtTheTimeObservable(innerCancellationTokenSource)
                        .Subscribe(
                            b =>
                            {

                            if (TextDataParser.IsCloseRecieved) return;

                            switch (DataReceiveMode)
                            {
                                case DataReceiveMode.IsListeningForHandShake:
                                    
                                    //TODO Create timer for managing time-out
                                    _handshakeParser.Parse(b, _parserDelgate, _parserHandler);

                                    HandshakeController(_parserDelgate,  subProtocols);
                                    break;

                                case DataReceiveMode.IsListeningForTextData:

                                    TextDataParser.Parse(_tcpSocketClient, b[0], ExcludeZeroApplicationDataInPong);

                                        if (TextDataParser.IsCloseRecieved)
                                        {
                                            StopReceivingData();
                                        }

                                    if (TextDataParser.HasNewMessage)
                                    {
                                        obs.OnNext(TextDataParser.NewMessage);
                                    }
                                    break;
                                }
                            },
                                ex =>
                                {
                                    StopReceivingData();
                                    _textMessageSequence.OnError(ex);
                                },
                                () =>
                                {
                                    StopReceivingData();
                                    _textMessageSequence.OnCompleted();
                                }
                            );

                    HasReceivedCloseFromServer = false;

                    return disp;
                });

            return observable;
        }

        private void HandshakeController(HttpParserDelegate parserDelegate, IEnumerable<string> subProtocols)
        {
            if (_parserDelgate.HttpRequestReponse.IsEndOfMessage)
            {
                if (parserDelegate.HttpRequestReponse.StatusCode == 101)
                {
                    if (subProtocols != null)
                    {
                        SubprotocolAccepted =
                            parserDelegate?.HttpRequestReponse?.Headers?.ContainsKey(
                                "SEC-WEBSOCKET-PROTOCOL") ?? false;

                        if (SubprotocolAccepted)
                        {
                            SubprotocolAcceptedName =
                                parserDelegate?.HttpRequestReponse?.Headers?["SEC-WEBSOCKET-PROTOCOL"];
                            if (!string.IsNullOrEmpty(SubprotocolAcceptedName))
                            {
                                Success();
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
                        Success();
                    }

                    void Success()
                    {
                        //_connectionStatusObserver.OnNext(ConnectionStatus.Connected);
                        System.Diagnostics.Debug.WriteLine("HandShake completed");
                        DataReceiveMode = DataReceiveMode.IsListeningForTextData;
                        IsConnected = true;
                    }
                }
                else
                {
                    throw new Exception($"Unable to connect to websocket Server. " +
                                        $"Error code: {parserDelegate.HttpRequestReponse.StatusCode}, " +
                                        $"Error reason: {parserDelegate.HttpRequestReponse.ResponseReason}");
                }
            }
            //else
            //{
            //    if (parserDelegate.HttpRequestReponse.IsRequestTimedOut)
            //    {
            //        _parserDelgate.HttpRequestReponse.IsRequestTimedOut = true;
            //        throw new TimeoutException("Connection request to server timed out");
            //    }

            //    _parserDelgate.HttpRequestReponse.IsUnableToParseHttp = true;
            //    throw new Exception("Invalid response from websocket server");
            //}
        }

        internal void StopReceivingData()
        {
            //_isConnected = false;
           // _byteStreamSessionSubscription.Dispose();
            HasReceivedCloseFromServer = true;
            _webSocketConnectService.Disconnect();
        }
    }
}
