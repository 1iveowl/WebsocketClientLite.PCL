﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectService
    {
        internal Stream TcpStream;

        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;

        private WebsocketParserHandler _websocketParserHandler;

        internal WebSocketConnectService(IObserver<ConnectionStatus> observerConnectionStatus)
        {
            _observerConnectionStatus = observerConnectionStatus;
        }

        internal async Task ConnectToWebSocketServer(
            WebsocketParserHandler websocketParserHandler,
            Uri uri,
            bool secure,
            CancellationToken token,
            Stream tcpStream,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            _websocketParserHandler = websocketParserHandler;

            TcpStream = tcpStream;

            _observerConnectionStatus.OnNext(ConnectionStatus.Connecting);

            await SendConnectHandShakeAsync(uri, secure, token, origin, headers, subprotocols);

            var waitForHandShakeResult = await _websocketParserHandler.ParserDelegate
                .HandshakeParserCompletionObservable
                .Timeout(TimeSpan.FromSeconds(30))
                .Catch<ParserState, TimeoutException>(tx => Observable.Return(ParserState.HandshakeTimedOut));

            switch (waitForHandShakeResult)
            {
                case ParserState.HandshakeCompletedSuccessfully:
                    _observerConnectionStatus.OnNext(ConnectionStatus.HandshakeCompletedSuccessfully);
                    break;
                case ParserState.HandshakeFailed:
                    throw new WebsocketClientLiteException("Unable to complete handshake");
                case ParserState.HandshakeTimedOut:
                    throw new WebsocketClientLiteException("Handshake timed out.");
                default:
                    throw new ArgumentOutOfRangeException($"Unknown parser state: {waitForHandShakeResult}");
            }
        }


        private async Task SendConnectHandShakeAsync(
            Uri uri, 
            bool secure,
            CancellationToken token,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocol = null
            )
        {
            var handShake = ClientHandShake.Compose(uri, secure, origin, headers, subprotocol);
            try
            {
                await TcpStream.WriteAsync(handShake, 0, handShake.Length, token);
                await TcpStream.FlushAsync(token);
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }
    }
}
