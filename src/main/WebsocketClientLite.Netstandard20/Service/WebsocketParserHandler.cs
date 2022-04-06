using HttpMachine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;
using System.Linq;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketParserHandler : IDisposable
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly IObserver<DataReceiveState> _observerDataReceiveMode;
        private readonly TextDataParser _textDataParser;

        private DataReceiveState _dataReceiveState;

        private bool ExcludeZeroApplicationDataInPong { get; }
        private bool IsSubprotocolAccepted { get; set; }

        internal readonly IObservable<DataReceiveState> DataReceiveStateObservable;
        internal IEnumerable<string> SubprotocolAcceptedNames { get; private set; }
        internal HttpWebSocketParserDelegate ParserDelegate { get; private set; }

        internal WebsocketParserHandler(            
            bool subprotocolAccepted,
            bool excludeZeroApplicationDataInPong,
            HttpWebSocketParserDelegate httpWebSocketParserDelegate,
            IObserver<ConnectionStatus> observerConnectionStatus)
        {
            var dataReceiveSubject = new BehaviorSubject<DataReceiveState>(DataReceiveState.Start);
            _textDataParser = new TextDataParser();

            _observerConnectionStatus = observerConnectionStatus;
            _observerDataReceiveMode = dataReceiveSubject.AsObserver();

            ParserDelegate = httpWebSocketParserDelegate;

            IsSubprotocolAccepted = subprotocolAccepted;
            ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;

            DataReceiveStateObservable = dataReceiveSubject.AsObservable(); 
        }
        internal IObservable<string> CreateWebsocketListenerObservable(
            Stream tcpStream,
            IEnumerable<string> subProtocols = null) =>
                Observable.Create<string>(obs =>
                {
                    _observerConnectionStatus.OnNext(ConnectionStatus.ConnectingToTcpSocket);

                    //var parserDelegate = new HttpWebSocketParserDelegate();
                    //ParserDelegate = new HttpWebSocketParserDelegate();
                    using var parserHandler = new HttpCombinedParser(ParserDelegate);

                    _textDataParser.Reinitialize();

                    _dataReceiveState = DataReceiveState.IsListeningForHandShake;
                    _observerDataReceiveMode.OnNext(_dataReceiveState);

                    var disposableReceiveState = DataReceiveStateObservable.Subscribe(s =>
                        {
                            _dataReceiveState = s;
                        },
                        obs.OnError,
                        () =>
                        {
                            Debug.WriteLine("DataReceiveObservable completed");
                        });

                    var disposableStreamListener =
                        Observable.While(
                            () => _dataReceiveState == DataReceiveState.IsListeningForHandShake
                                || _dataReceiveState == DataReceiveState.IsListening,
                            Observable.FromAsync(() => ReadOneByteAtTheTimeAsync(tcpStream)))
                        .Select(b =>
                            Observable.FromAsync(() =>
                                ParseWebSocketAsync(
                                    b,
                                    tcpStream, 
                                    ParserDelegate, 
                                    parserHandler, 
                                    subProtocols, 
                                    obs)))
                        .Concat()
                        .Subscribe(
                        _ =>
                        {
                            if (_textDataParser.IsCloseReceived)
                            {
                                _observerDataReceiveMode.OnNext(DataReceiveState.Exiting);
                                _observerDataReceiveMode.OnCompleted();
                            }
                        },
                        obs.OnError,
                        obs.OnCompleted);

                    return new CompositeDisposable(disposableReceiveState, disposableStreamListener);
                })
            .Publish().RefCount();

        private async Task ParseWebSocketAsync(
            byte[] byteArray, 
            Stream tcpStream,
            HttpWebSocketParserDelegate parserDelegate, 
            HttpCombinedParser parserHandler,
            IEnumerable<string> subProtocols,
            IObserver<string> obs)
        {

            switch (_dataReceiveState)
            {
                case DataReceiveState.IsListeningForHandShake:

                    //var handshakeParser = new HandshakeParser();
                    //handshakeParser.Parse(b, parserDelegate, parserHandler);

                    parserHandler.Execute(byteArray);

                    HandshakeController(parserDelegate, subProtocols);
                    break;

                case DataReceiveState.IsListening:

                    await _textDataParser.ParseAsync(tcpStream, byteArray[0], ExcludeZeroApplicationDataInPong);

                    if (_textDataParser.IsCloseReceived)
                    {
                        StopReceivingData();
                    }

                    if (_textDataParser.HasNewMessage)
                    {
                        obs.OnNext(_textDataParser.NewMessage);
                    }

                    break;
            }
        }

        private void HandshakeController(
            HttpWebSocketParserDelegate parserDelegate, 
            IEnumerable<string> subProtocols)
        {
            if (parserDelegate.HttpRequestResponse is not null 
                && parserDelegate.HttpRequestResponse.IsEndOfMessage)
            {
                if (parserDelegate.HttpRequestResponse.StatusCode == 101)
                {
                    if (subProtocols is not null)
                    {
                        IsSubprotocolAccepted =
                            parserDelegate?.HttpRequestResponse?.Headers?.ContainsKey(
                                "SEC-WEBSOCKET-PROTOCOL") ?? false;

                        if (IsSubprotocolAccepted)
                        {
                            SubprotocolAcceptedNames =
                                parserDelegate?.HttpRequestResponse?.Headers?["SEC-WEBSOCKET-PROTOCOL"];

                            if (!SubprotocolAcceptedNames?.Any(sp => sp.Length > 0) ?? false)
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

                    _observerConnectionStatus.OnNext(ConnectionStatus.WebsocketConnected);
                    Debug.WriteLine("HandShake completed");
                    _observerDataReceiveMode.OnNext(DataReceiveState.IsListening);

                }
                else
                {
                    throw new WebsocketClientLiteException($"Unable to connect to websocket Server. " +
                                        $"Error code: {parserDelegate.HttpRequestResponse.StatusCode}, " +
                                        $"Error reason: {parserDelegate.HttpRequestResponse.ResponseReason}");
                }
            }
        }

        private void StopReceivingData()
        {
            _observerDataReceiveMode.OnNext(DataReceiveState.Exiting);
            _observerDataReceiveMode.OnCompleted();
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

        public void Dispose()
        {
            //_tcpStream?.Dispose();
            _textDataParser?.Dispose();
            ParserDelegate?.Dispose();
        }
    }
}
