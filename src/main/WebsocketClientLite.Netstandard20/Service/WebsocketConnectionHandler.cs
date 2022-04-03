using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Model;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketConnectionHandler : IDisposable
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly IObserver<string> _observerMessage;

        private readonly WebsocketParserHandler _websocketParserHandler;
        private readonly TcpConnectionService _tcpConnectionService;

        internal WebsocketSenderHandler WebsocketSenderHandler { get; private set; }

        internal WebsocketConnectionHandler(
            TcpConnectionService tcpConnectionService,
            WebsocketParserHandler websocketParserHandler,
            IObserver<ConnectionStatus> observerConnectionStatus,
            IObserver<string> observerMessage)
        {
            _tcpConnectionService = tcpConnectionService;
            _observerConnectionStatus = observerConnectionStatus;
            _observerMessage = observerMessage;
            _websocketParserHandler = websocketParserHandler;
        }

        internal async Task ConnectToWebSocketServer(
            Uri uri,            
            X509CertificateCollection x509CertificateCollection,
            SslProtocols tlsProtocolType,
            TimeSpan timeout = default,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            var stream = await _tcpConnectionService.Connect(
                uri, 
                () => _observerConnectionStatus.OnNext(ConnectionStatus.TcpSocketConnected),
                x509CertificateCollection,
                tlsProtocolType,
                timeout);

            WebsocketSenderHandler = new WebsocketSenderHandler(_observerConnectionStatus, stream);

            try
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.SendingHandshakeToWebsocketServer);
                await WebsocketSenderHandler.SendConnectHandShakeAsync(uri, origin, headers, subprotocols);                
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnError(ex);
            }            

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
                await WebsocketSenderHandler.SendCloseHandshakeAsync(StatusCodes.GoingAway).ToObservable().Timeout(TimeSpan.FromSeconds(5));
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

        public void Dispose()
        {
            _websocketParserHandler.Dispose();
            _tcpConnectionService.Dispose();
        }
    }
}