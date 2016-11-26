using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using HttpMachine;
using ISocketLite.PCL.Interface;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketListener
    {
        private HttpParserDelegate _parserDelgate;
        private HttpCombinedParser _parserHandler;
        private readonly TextDataParser _textDataParser;
        private readonly HandshakeParser _handshakeParser = new HandshakeParser();
        private readonly WebSocketConnectService _webSocketConnectService;

        private IDisposable _websocketDataSubscription;

        private readonly IConnectableObservable<byte[]> _observableWebsocketData;

        internal IObservable<string> ObserveTextMessageSequence => _observableWebsocketData.Select(
            b =>
            {
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

                        _textDataParser.Parse(b[0]);

                        if (_textDataParser.IsCloseRecieved)
                        {
                            Stop();
                        }
                        return _textDataParser.HasNewMessage ? _textDataParser.NewMessage : null;

                    default:
                        return null;
                }
            }).Where(str => !string.IsNullOrEmpty(str));

        internal DataReceiveMode DataReceiveMode { get; set; } = DataReceiveMode.IsListeningForHandShake;

        internal bool HasReceivedCloseFromServer { get; private set; }

        internal WebsocketListener(
            ITcpSocketClient client,
            IConnectableObservable<byte[]> observableWebsocketData,
            WebSocketConnectService webSocketConnectService)
        {
            _webSocketConnectService = webSocketConnectService;
            _observableWebsocketData = observableWebsocketData;
            _textDataParser = new TextDataParser(client);
        }

        internal void Start(HttpParserDelegate requestHandler, HttpCombinedParser parserHandler)
        {
            _parserHandler = parserHandler;
            _parserDelgate = requestHandler;
            _textDataParser.Reinitialize();

            _websocketDataSubscription = _observableWebsocketData.Connect();

            HasReceivedCloseFromServer = false;
        }

        internal void Stop()
        {
            _websocketDataSubscription.Dispose();
            HasReceivedCloseFromServer = true;
            _webSocketConnectService.Disconnect();
        }
    }
}
