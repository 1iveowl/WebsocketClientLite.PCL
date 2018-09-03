using HttpMachine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketListener
    {
        private readonly ISubject<ConnectionStatus> _subjectConnectionStatus;

        private readonly HandshakeParser _handshakeParser = new HandshakeParser();
        private readonly WebSocketConnectService _webSocketConnectService;
        internal readonly TextDataParser TextDataParser;

        private Stream _tcpStream;
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
            Stream tcpStream,
            IEnumerable<string> subProtocols = null)
        {
            var parserDelegate = new HttpParserDelegate();
            var parserHandler = new HttpCombinedParser(parserDelegate);
            TextDataParser.Reinitialize();

            _innerCancellationTokenSource = innerCancellationTokenSource;

            _tcpStream = tcpStream;

            DataReceiveMode = DataReceiveMode.IsListeningForHandShake;
            IsConnected = false;
            _isHandshaking = true;

            WatchHandshakeForTimeout(parserDelegate, _innerCancellationTokenSource);

            var listenerObservable = Observable.Create<string>(
                obs =>
                {
                    var disposableStreamListener = Observable.While(
                            () => IsConnected || _isHandshaking,
                            Observable.FromAsync(() => ReadOneByteAtTheTimeAsync(tcpStream)))
                        .Where(p => !TextDataParser.IsCloseRecieved)
                        .Select(b => Observable.FromAsync(() => ParseWebSocketAsync(b, parserDelegate, parserHandler, subProtocols, obs)))
                        .Concat()
                        .Subscribe(
                        _ =>
                        {

                        },
                        ex =>
                        {
                            StopReceivingData();
                            obs.OnError(ex);
                        },
                        () =>
                        {
                            StopReceivingData();
                            obs.OnCompleted();
                        });

                    HasReceivedCloseFromServer = false;

                    return disposableStreamListener;
                });

            return listenerObservable;
        }

        private async Task ParseWebSocketAsync(
            byte[] b, 
            HttpParserDelegate parserDelegate, 
            HttpCombinedParser parserHandler,
            IEnumerable<string> subProtocols,
            IObserver<string> obs)
        {
            if (_hasHandshakeTimedout)
            {
                _subjectConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Connection request to server timed out");
            }

            switch (DataReceiveMode)
            {
                case DataReceiveMode.IsListeningForHandShake:

                    _handshakeParser.Parse(b, parserDelegate, parserHandler);

                    HandshakeController(parserDelegate, subProtocols);
                    break;

                case DataReceiveMode.IsListeningForTextData:

                    await TextDataParser.ParseAsync(_tcpStream, b[0], ExcludeZeroApplicationDataInPong);

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
                                _subjectConnectionStatus.OnNext(ConnectionStatus.Aborted);
                                throw new WebsocketClientLiteException("Server responded with blank Sub Protocol name");
                            }
                        }
                        else
                        {
                            _subjectConnectionStatus.OnNext(ConnectionStatus.Aborted);
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

        private async Task<byte[]> ReadOneByteAtTheTimeAsync(Stream stream) //, CancellationTokenSource cancellationTokenSource)
        {
            //if (!isConnectionOpen) return null;

            var oneByteArray = new byte[1];

            try
            {
                if (stream == null)
                {
                    throw new WebsocketClientLiteException("Readstream cannot be null.");
                }

                if (!stream.CanRead)
                {
                    throw new WebsocketClientLiteException("Websocket connection have been closed.");
                }

                var bytesRead = await stream.ReadAsync(oneByteArray, 0, 1);

                if (bytesRead < oneByteArray.Length)
                {
                    //cancellationTokenSource.Cancel();
                    throw new WebsocketClientLiteException("Websocket connection aborted unexpectantly. Check connection and socket security version/TLS version).");
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
            }
            return oneByteArray;
        }
    }
}
