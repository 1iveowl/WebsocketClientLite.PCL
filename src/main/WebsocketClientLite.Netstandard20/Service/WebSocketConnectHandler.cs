using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectHandler
    {
        internal Stream TcpStream;

        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly IObserver<string> _observerMessage;

        private WebsocketParserHandler _websocketParserHandler;
        private WebsocketSenderHandler _websocketSenderHandler;

        internal WebSocketConnectHandler(
            IObserver<ConnectionStatus> observerConnectionStatus,
            IObserver<string> observerMessage)
        {
            _observerConnectionStatus = observerConnectionStatus;
            _observerMessage = observerMessage;

        }

        internal async Task ConnectToWebSocketServer(
            WebsocketParserHandler websocketParserHandler,
            WebsocketSenderHandler websocketSenderHandler,
            Uri uri,
            Stream tcpStream,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            _websocketParserHandler = websocketParserHandler;
            _websocketSenderHandler = websocketSenderHandler;

            TcpStream = tcpStream;

            _observerConnectionStatus.OnNext(ConnectionStatus.HandshakeSendToWebsocketServer);

            await SendConnectHandShakeAsync(uri, origin, headers, subprotocols);

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

        internal async Task DisconnectWebsocketServer()
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Disconnecting);

            try
            {
                await _websocketSenderHandler.SendCloseHandshakeAsync(TcpStream, StatusCodes.GoingAway).ToObservable().Timeout(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                throw new WebsocketClientLiteException("Unable to disconnect.", ex);
            }

            try
            {
                await _websocketParserHandler.DataReceiveStateObservable.Timeout(TimeSpan.FromSeconds(10));
            }
            catch (InvalidOperationException)
            {
                Debug.WriteLine("Ignore. Already disconnected.");
            }
            catch (TimeoutException ex)
            {
                throw new WebsocketClientLiteException("Disconnect timed out. Unable to disconnect gracefully.", ex);
            }
            catch (Exception ex)
            {
                throw new WebsocketClientLiteException("Unable to disconnect gracefully.", ex);
            }
            finally
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Disconnected);
                _observerConnectionStatus.OnCompleted();
                _observerMessage.OnCompleted();
            }
        }

        private async Task SendConnectHandShakeAsync(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocol = null,
            bool isSocketIOv4 = false
        )
        {
            var handShake = ClientHandShake.Compose(uri, origin, headers, subprotocol, isSocketIOv4);

            try
            {
                await TcpStream.WriteAsync(handShake, 0, handShake.Length);
                await TcpStream.FlushAsync();
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }
    }
}