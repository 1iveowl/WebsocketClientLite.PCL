using HttpMachine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketListener
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;

        private readonly HandshakeParser _handshakeParser = new HandshakeParser();
        internal readonly TextDataParser TextDataParser;

        private Stream _tcpStream;
        private CancellationTokenSource _innerCancellationTokenSource;

        private bool _isHandshaking;

        private bool _hasHandshakeTimedOut;

        internal bool ExcludeZeroApplicationDataInPong;
        internal bool IsConnected { get; set; }

        internal bool SubprotocolAccepted { get; private set; }

        internal string SubprotocolAcceptedName { get; private set; }

        private DataReceiveMode DataReceiveMode { get; set; } = DataReceiveMode.IsListeningForHandShake;

        internal bool HasReceivedCloseFromServer { get; private set; }

        internal WebsocketListener(IObserver<ConnectionStatus> observerConnectionStatus)
        {
            _observerConnectionStatus = observerConnectionStatus;
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

            //await WatchHandshakeForTimeoutAsync(parserDelegate, _innerCancellationTokenSource);

            var listenerObservable = Observable.Create<string>(
                obs =>
                {
                    var streamListenerObservable = Observable.While(
                            () =>
                            {
                                return IsConnected || _isHandshaking;
                            },
                            Observable.FromAsync(() => ReadOneByteAtTheTimeAsync(tcpStream)))
                        .Where(b =>
                        {
                            return !TextDataParser.IsCloseRecieved;
                        })
                        .Select(b => Observable.FromAsync(() =>
                            ParseWebSocketAsync(b, parserDelegate, parserHandler, subProtocols, obs)))
                        .Concat();
                        

                    var disposableStreamListener = streamListenerObservable
                        .SelectMany(x => streamListenerObservable)
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

                    var handshakeObservable = Observable.FromAsync(() =>
                        WatchHandshakeForTimeoutAsync(parserDelegate, _innerCancellationTokenSource)).Subscribe();

                    HasReceivedCloseFromServer = false;

                    return new CompositeDisposable(handshakeObservable, disposableStreamListener);
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
            if (_hasHandshakeTimedOut)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
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
                                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                                throw new WebsocketClientLiteException("Server responded with blank Sub Protocol name");
                            }
                        }
                        else
                        {
                            _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                            throw new WebsocketClientLiteException("Server did not support any of the needed Sub Protocols");
                        }
                    }
                    else
                    {
                        Success();
                    }

                    void Success()
                    {
                        _observerConnectionStatus.OnNext(ConnectionStatus.Connected);
                       Debug.WriteLine("HandShake completed");
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

        private async Task WatchHandshakeForTimeoutAsync(HttpParserDelegate parserDelegate, CancellationTokenSource cancellationTokenSource)
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

            var taskRerun = await Task.WhenAny(handshakeTimer, timeout);

            if (taskRerun == timeout)
            {
                _hasHandshakeTimedOut = true;
            }
        }

        internal void StopReceivingData()
        {
            HasReceivedCloseFromServer = true;
        }

        private async Task<byte[]> ReadOneByteAtTheTimeAsync(Stream stream)
        {
            var oneByteArray = new byte[1];

            try
            {
                if (stream == null)
                {
                    throw new WebsocketClientLiteException("Read stream cannot be null.");
                }

                if (!stream.CanRead)
                {
                    throw new WebsocketClientLiteException("Websocket connection have been closed.");
                }

                var bytesRead = await stream.ReadAsync(oneByteArray, 0, 1);

                if (bytesRead < oneByteArray.Length)
                {
                    throw new WebsocketClientLiteException("Websocket connection aborted unexpectedly. Check connection and socket security version/TLS version).");
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
