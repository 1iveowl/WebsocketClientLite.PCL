using HttpMachine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketParserHandler : IDisposable
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly IObserver<DataReceiveState> _observerDataReceiveMode;
        private readonly HandshakeParser _handshakeParser;
        private readonly TextDataParser _textDataParser;

        private DataReceiveState _dataReceiveState;
        private Stream _tcpStream;
        private bool ExcludeZeroApplicationDataInPong { get; }
        private bool SubprotocolAccepted { get; set; }

        internal readonly IObservable<DataReceiveState> DataReceiveStateObservable;
        internal string SubprotocolAcceptedName { get; private set; }
        internal HttpParserDelegate ParserDelegate { get; private set; }

        internal WebsocketParserHandler(
            IObserver<ConnectionStatus> observerConnectionStatus,
            bool subprotocolAccepted,
            bool excludeZeroApplicationDataInPong)
        {
            _handshakeParser = new HandshakeParser();
            var dataReceiveSubject = new BehaviorSubject<DataReceiveState>(DataReceiveState.Start);
            _textDataParser = new TextDataParser();

            _observerConnectionStatus = observerConnectionStatus;
            _observerDataReceiveMode = dataReceiveSubject.AsObserver();

            SubprotocolAccepted = subprotocolAccepted;
            ExcludeZeroApplicationDataInPong = excludeZeroApplicationDataInPong;

            DataReceiveStateObservable = dataReceiveSubject.AsObservable(); 
        }
        
        internal IObservable<string> CreateWebsocketListenerObservable(
            Stream tcpStream,
            IEnumerable<string> subProtocols = null)
        {
            ParserDelegate = new HttpParserDelegate();
            var parserHandler = new HttpCombinedParser(ParserDelegate);

            _textDataParser.Reinitialize();

            _tcpStream = tcpStream;

            _observerDataReceiveMode.OnNext(DataReceiveState.IsListeningForHandShake);

            _dataReceiveState = DataReceiveState.IsListeningForHandShake;

            var listenerObservable = Observable.Create<string>(
                obs =>
                {
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
                        .Select(b => Observable.FromAsync(() => ParseWebSocketAsync(b, ParserDelegate, parserHandler, subProtocols, obs)))
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

            switch (_dataReceiveState)
            {
                case DataReceiveState.IsListeningForHandShake:

                    _handshakeParser.Parse(b, parserDelegate, parserHandler);

                    HandshakeController(parserDelegate, subProtocols);
                    break;

                case DataReceiveState.IsListening:

                    await _textDataParser.ParseAsync(_tcpStream, b[0], ExcludeZeroApplicationDataInPong);

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

        private void HandshakeController(HttpParserDelegate parserDelegate, IEnumerable<string> subProtocols)
        {
            if (parserDelegate.HttpRequestResponse.IsEndOfMessage)
            {
                if (parserDelegate.HttpRequestResponse.StatusCode == 101)
                {
                    if (subProtocols != null)
                    {
                        SubprotocolAccepted =
                            parserDelegate?.HttpRequestResponse?.Headers?.ContainsKey(
                                "SEC-WEBSOCKET-PROTOCOL") ?? false;

                        if (SubprotocolAccepted)
                        {
                            SubprotocolAcceptedName =
                                parserDelegate?.HttpRequestResponse?.Headers?["SEC-WEBSOCKET-PROTOCOL"];
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
                        _observerConnectionStatus.OnNext(ConnectionStatus.WebsocketConnected);
                        Debug.WriteLine("HandShake completed");
                        _observerDataReceiveMode.OnNext(DataReceiveState.IsListening);
                        //DataReceiveState = DataReceiveState.IsListeningForTextData;
                        //IsConnected = true;
                    }
                }
                else
                {
                    throw new WebsocketClientLiteException($"Unable to connect to websocket Server. " +
                                        $"Error code: {parserDelegate.HttpRequestResponse.StatusCode}, " +
                                        $"Error reason: {parserDelegate.HttpRequestResponse.ResponseReason}");
                }
            }
        }
        internal void StopReceivingData()
        {
            _observerDataReceiveMode.OnNext(DataReceiveState.Exiting);
            _observerDataReceiveMode.OnCompleted();
            //HasReceivedCloseFromServer = true;
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
                    throw new WebsocketClientLiteException("Websocket connection aborted expectantly. Check connection and socket security version/TLS version).");
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
            _tcpStream?.Dispose();
            _textDataParser?.Dispose();
            ParserDelegate?.Dispose();
        }
    }
}
