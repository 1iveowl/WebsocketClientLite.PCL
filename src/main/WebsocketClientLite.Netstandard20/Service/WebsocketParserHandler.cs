using HttpMachine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketParserHandler
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;

        private readonly HandshakeParser _handshakeParser = new HandshakeParser();

        private DataReceiveMode DataReceiveMode { get; set; } = DataReceiveMode.IsListeningForHandShake;

        private Stream _tcpStream;

        private bool _isHandshaking;

        internal readonly TextDataParser TextDataParser;

        internal bool ExcludeZeroApplicationDataInPong;
        internal bool IsConnected { get; set; }

        internal bool SubprotocolAccepted { get; private set; }

        internal string SubprotocolAcceptedName { get; private set; }

        internal HttpParserDelegate ParserDelegate { get; private set; }


        internal bool HasReceivedCloseFromServer { get; private set; }

        internal WebsocketParserHandler(IObserver<ConnectionStatus> observerConnectionStatus)
        {
            _observerConnectionStatus = observerConnectionStatus;
            TextDataParser = new TextDataParser();
        }
        
        internal IObservable<string> CreateWebsocketListenerObservable(
            CancellationTokenSource innerCancellationTokenSource,
            Stream tcpStream,
            IEnumerable<string> subProtocols = null)
        {
            ParserDelegate = new HttpParserDelegate();
            var parserHandler = new HttpCombinedParser(ParserDelegate);

            TextDataParser.Reinitialize();

            _tcpStream = tcpStream;

            DataReceiveMode = DataReceiveMode.IsListeningForHandShake;
            IsConnected = false;
            _isHandshaking = true;

            //WatchHandshakeForTimeout(parserDelegate, _innerCancellationTokenSource);

            var listenerObservable = Observable.Create<string>(
                obs =>
                {
                    var disposableStreamListener = 
                        Observable.While(
                            () => IsConnected || _isHandshaking,
                            Observable.FromAsync(() => ReadOneByteAtTheTimeAsync(tcpStream)))
                        .Where(p => !TextDataParser.IsCloseRecieved)
                        .Select(b => Observable.FromAsync(() => ParseWebSocketAsync(b, ParserDelegate, parserHandler, subProtocols, obs)))
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
                        System.Diagnostics.Debug.WriteLine("HandShake completed");
                        DataReceiveMode = DataReceiveMode.IsListeningForTextData;
                        IsConnected = true;
                        _isHandshaking = false;
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
                    throw new WebsocketClientLiteException("Websocket connection aborted expectantly. Check connection and socket security version/TLS version).");
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
