using System;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using HttpMachine;
using ISocketLite.PCL.Interface;
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

        private IObservable<string> ObserveTextMessageSession => ByteStreamHandlerObservable.Select(
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

                        TextDataParser.Parse(_tcpSocketClient, b[0]);

                        if (TextDataParser.IsCloseRecieved)
                        {
                            StopReceivingData();
                        }
                        return TextDataParser.HasNewMessage ? TextDataParser.NewMessage : null;

                    default:
                        return null;
                }
            }).Where(str => !string.IsNullOrEmpty(str));

        private IObservable<byte[]> ByteStreamHandlerObservable => Observable.While(
                () => !_innerCancellationTokenSource.IsCancellationRequested,
                Observable.FromAsync(ReadOneByteAtTheTimeAsync))
            .ObserveOn(Scheduler.Default);

        internal bool IsConnected;

        internal IObservable<string> ObserveTextMessageSequence => _textMessageSequence.AsObservable();
        internal DataReceiveMode DataReceiveMode { get; set; } = DataReceiveMode.IsListeningForHandShake;
        internal bool HasReceivedCloseFromServer { get; private set; }

        internal WebsocketListener(

            WebSocketConnectService webSocketConnectService)
        {
            _webSocketConnectService = webSocketConnectService;
            TextDataParser = new TextDataParser();
        }

        private async Task<byte[]> ReadOneByteAtTheTimeAsync()
        {
            if (TextDataParser.IsCloseRecieved) return null;

            var oneByteArray = new byte[1];

            try
            {
                if (!_webSocketConnectService?.TcpSocketClient?.ReadStream?.CanRead ?? false)
                {
                    throw new Exception("Websocket connection have been closed");
                }

                var bytesRead = await _webSocketConnectService.TcpSocketClient.ReadStream.ReadAsync(oneByteArray, 0, 1);

                Debug.WriteLine(oneByteArray[0].ToString());

                if (bytesRead < oneByteArray.Length)
                {
                    IsConnected = false;
                    _innerCancellationTokenSource.Cancel();
                    throw new Exception("Websocket connection aborted unexpectantly. Check connection and socket security version/TLS version)");
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
            }
            return oneByteArray;
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

        internal void StopReceivingData()
        {
            IsConnected = false;
            _byteStreamSessionSubscription.Dispose();
            HasReceivedCloseFromServer = true;
            _webSocketConnectService.Disconnect();
        }


    }
}
