using HttpMachine;
using ISocketLite.PCL.Interface;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketListener
    {
        #region Obsolete

        //private IDisposable _byteStreamSessionSubscription;

        //[Obsolete("Deprecated")]
        //private readonly ISubject<string> _textMessageSequence = new Subject<string>();

        //[Obsolete("Deprecated")]
        //internal IObservable<string> ObserveTextMessageSequence => _textMessageSequence.AsObservable();

        //[Obsolete("Deprecated")]
        //private IObservable<string> ObserveTextMessageSession => _tcpSocketClient
        //    .ReadStream.ReadOneByteAtTheTimeObservable(_innerCancellationTokenSource, IsConnected || _isHandshaking)
        //    .Select(
        //        b =>
        //        {
        //            if (TextDataParser.IsCloseRecieved) return string.Empty;

        //            switch (DataReceiveMode)
        //            {
        //                case DataReceiveMode.IsListeningForHandShake:
        //                    try
        //                    {
        //                        if (_parserDelegate.HttpRequestReponse.IsEndOfMessage)
        //                        {
        //                            return string.Empty;
        //                        }

        //                        _handshakeParser.Parse(b, _parserDelegate, _parserHandler);
        //                        return null;
        //                    }
        //                    catch (Exception ex)
        //                    {
        //                        if (ex is TimeoutException)
        //                        {
        //                            _parserDelegate.HttpRequestReponse.IsRequestTimedOut = true;
        //                        }
        //                        else
        //                        {
        //                            _parserDelegate.HttpRequestReponse.IsUnableToParseHttp = true;
        //                        }
        //                        return null;
        //                    }
        //                case DataReceiveMode.IsListeningForTextData:

        //                    TextDataParser.Parse(_tcpSocketClient, b[0], ExcludeZeroApplicationDataInPong);

        //                    if (TextDataParser.IsCloseRecieved)
        //                    {
        //                        StopReceivingData();
        //                    }
        //                    return TextDataParser.HasNewMessage ? TextDataParser.NewMessage : null;

        //                default:
        //                    return null;
        //            }
        //        }).Where(str => !string.IsNullOrEmpty(str));


        //[Obsolete("Deprecated")]
        //internal void Start(
        //    HttpParserDelegate requestHandler,
        //    HttpCombinedParser parserHandler,
        //    CancellationTokenSource innerCancellationTokenSource)
        //{
        //    _parserHandler = parserHandler;
        //    _parserDelegate = requestHandler;
        //    TextDataParser.Reinitialize();
        //    _innerCancellationTokenSource = innerCancellationTokenSource;

        //    _tcpSocketClient = _webSocketConnectService.TcpSocketClient;

        //    _byteStreamSessionSubscription = ObserveTextMessageSession.Subscribe(
        //        str =>
        //        {
        //            _textMessageSequence.OnNext(str);
        //        },
        //        ex =>
        //        {
        //            _textMessageSequence.OnError(ex);
        //        },
        //        () =>
        //        {
        //            _textMessageSequence.OnCompleted();
        //        });

        //    HasReceivedCloseFromServer = false;
        //}

        #endregion

        private readonly ISubject<ConnectionStatus> _subjectConnectionStatus;

        private readonly HandshakeParser _handshakeParser = new HandshakeParser();
        private readonly WebSocketConnectService _webSocketConnectService;
        internal readonly TextDataParser TextDataParser;

        private ITcpSocketClient _tcpSocketClient;
        private CancellationTokenSource _innerCancellationTokenSource;

        private bool _isHandshaking;

        private bool _hasHandshakeTimedout;

        internal bool ExcludeZeroApplicationDataInPong;
        internal bool IsConnected { get; set; }

        internal bool SubprotocolAccepted { get; private set; }

        internal string SubprotocolAcceptedName { get; private set; }

        private DataReceiveMode DataReceiveMode { get; set; } = DataReceiveMode.IsListeningForHandShake;

        internal bool HasReceivedCloseFromServer { get; private set; }

        internal WebsocketListener(WebSocketConnectService webSocketConnectService, ISubject<ConnectionStatus> subjectConnectionStatus)
        {
            _webSocketConnectService = webSocketConnectService;
            _subjectConnectionStatus = subjectConnectionStatus;
            TextDataParser = new TextDataParser();
        }
        
        internal IObservable<string> CreateObservableListener(
            CancellationTokenSource innerCancellationTokenSource,
            ITcpSocketClient tcpSocketClient,
            IEnumerable<string> subProtocols = null)
        {
            var parserDelegate = new HttpParserDelegate();
            var parserHandler = new HttpCombinedParser(parserDelegate);
            TextDataParser.Reinitialize();

            _innerCancellationTokenSource = innerCancellationTokenSource;

            _tcpSocketClient = tcpSocketClient;

            DataReceiveMode = DataReceiveMode.IsListeningForHandShake;
            IsConnected = false;
            _isHandshaking = true;

            WatchHandshakeForTimeout(parserDelegate, _innerCancellationTokenSource);

            var observable = Observable.Create<string>(
                obs =>
                {
                    var disp = _tcpSocketClient.ReadStream.ReadOneByteAtTheTimeObservable(innerCancellationTokenSource, isConnectionOpen:IsConnected || _isHandshaking)
                        .Subscribe(
                            b =>
                            {
                                if (TextDataParser.IsCloseRecieved) return;

                                if (_hasHandshakeTimedout)
                                {
                                    throw new WebsocketClientLiteException("Connection request to server timed out");
                                }

                                switch (DataReceiveMode)
                                {
                                    case DataReceiveMode.IsListeningForHandShake:

                                        _handshakeParser.Parse(b, parserDelegate, parserHandler);

                                        HandshakeController(parserDelegate,  subProtocols);
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
                                        throw ex;
                                    },
                                    () =>
                                    {
                                        // ReSharper disable once ConvertClosureToMethodGroup
                                        StopReceivingData();
                                    }
                            );

                    HasReceivedCloseFromServer = false;

                    return disp;
                });

            return observable;
        }

        private void HandshakeController(HttpParserDelegate parserDelegate, IEnumerable<string> subProtocols)
        {
            if (parserDelegate.HttpRequestReponse.IsEndOfMessage)
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
                                throw new WebsocketClientLiteException("Server responded with blank Sub Protocol name");
                            }
                        }
                        else
                        {
                            throw new WebsocketClientLiteException("Server did not support any of the needed Sub Protocols");
                        }
                    }
                    else
                    {
                        Success();
                    }

                    void Success()
                    {
                        _subjectConnectionStatus.OnNext(ConnectionStatus.Connected);
                        System.Diagnostics.Debug.WriteLine("HandShake completed");
                        DataReceiveMode = DataReceiveMode.IsListeningForTextData;
                        IsConnected = true;
                        _isHandshaking = false;
                    }
                }
                else
                {
                    throw new WebsocketClientLiteException($"Unable to connect to websocket Server. " +
                                        $"Error code: {parserDelegate.HttpRequestReponse.StatusCode}, " +
                                        $"Error reason: {parserDelegate.HttpRequestReponse.ResponseReason}");
                }
            }
        }

        private async void WatchHandshakeForTimeout(HttpParserDelegate parserDelegate, CancellationTokenSource cancellationTokenSource)
        {
            var handshakeTimer = Task.Run(async () =>
            {
                while (!parserDelegate.HttpRequestReponse.IsEndOfMessage
                       && !parserDelegate.HttpRequestReponse.IsRequestTimedOut
                       && !parserDelegate.HttpRequestReponse.IsUnableToParseHttp)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                }
            }, cancellationTokenSource.Token);

            var timeout = Task.Delay(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);

            var taskRetun = await Task.WhenAny(handshakeTimer, timeout);

            if (taskRetun == timeout)
            {
                _hasHandshakeTimedout = true;
            }
        }

        internal void StopReceivingData()
        {
            HasReceivedCloseFromServer = true;
        }
    }
}
