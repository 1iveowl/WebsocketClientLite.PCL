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
using System.IO;
using WebsocketClientLite.PCL.Extension;
using System.Reactive.Concurrency;
using System.Reactive;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketConnectionHandler : IDisposable
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly WebsocketParserHandler _websocketParserHandler;
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly Func<Stream, IObserver<ConnectionStatus>, WebsocketSenderHandler> _createWebsocketSenderFunc;

        //internal WebsocketSenderHandler WebsocketSenderHandler { get; private set; }

        internal WebsocketConnectionHandler(
            TcpConnectionService tcpConnectionService,
            WebsocketParserHandler websocketParserHandler,
            IObserver<ConnectionStatus> observerConnectionStatus,
            Func<Stream, IObserver<ConnectionStatus>, WebsocketSenderHandler> createWebsocketSenderFunc)
        {
            _tcpConnectionService = tcpConnectionService;
            _observerConnectionStatus = observerConnectionStatus;
            _websocketParserHandler = websocketParserHandler;
            _createWebsocketSenderFunc = createWebsocketSenderFunc;
        }

        internal async Task<IObservable<string>>
                ConnectWebsocket(
                    Uri uri,
                    X509CertificateCollection x509CertificateCollection,
                    SslProtocols tlsProtocolType,
                    Action<ISender> setSenderAction,
                    TimeSpan timeout = default,
                    string origin = null,
                    IDictionary<string, string> headers = null,
                    IEnumerable<string> subprotocols = null)
        {
            var stream = await _tcpConnectionService.ConnectStream(
                uri, 
                () => _observerConnectionStatus.OnNext(ConnectionStatus.TcpSocketConnected),
                x509CertificateCollection,
                tlsProtocolType,
                timeout);

            var sender = _createWebsocketSenderFunc(stream, _observerConnectionStatus);

            var listener = _websocketParserHandler
                .CreateWebsocketListenerObservable(stream, subprotocols)
                // https://stackoverflow.com/a/45217578/4140832
                .FinallyAsync(async () =>
                {
                    await DisconnectWebsocket(sender);
                });

            //_handshakeListener = _websocketParserHandler
            //    .CreateWebsocketListenerObservable(stream, Subprotocols);

            //try
            //{
            //    _observerConnectionStatus.OnNext(ConnectionStatus.SendingHandshakeToWebsocketServer);
            //    await WebsocketSenderHandler.SendConnectHandShakeAsync(
            //        uri, 
            //        origin, 
            //        headers, 
            //        _websocketParserHandler.SubprotocolAcceptedNames);                
            //}
            //catch (Exception ex)
            //{
            //    _observerConnectionStatus.OnError(ex);
            //}

            await SendHandShake(
                uri,
                sender,
                stream,
                sender,
                setSenderAction,
                origin,
                headers,
                subprotocols);

            return listener;
        }

        private async Task SendHandShake(
            Uri uri,
            WebsocketSenderHandler websocketSenderHandler,
            Stream stream,
            ISender sender,
            Action<ISender> setSenderAction,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            var handshakeListener = _websocketParserHandler
                .CreateWebsocketListenerObservable(stream, subprotocols);

            //var scheduler = DefaultScheduler.Instance;

            //var handshakeObservable = _websocketParserHandler
            //    .ParserDelegate
            //    .HandshakeParserCompletionObservable
            //    .Timeout(TimeSpan.FromSeconds(30))
            //    .Catch<ParserState, TimeoutException>(tx => Observable.Return(ParserState.HandshakeTimedOut))
            //    .ToTask();

            //var sendObservable = websocketSenderHandler.SendConnectHandShake(
            //    uri,
            //    origin,
            //    headers,
            //    _websocketParserHandler.SubprotocolAcceptedNames);

            var handshakeListnerDisposable = handshakeListener
                .Subscribe(_ =>
                {

                },
                ex =>
                {

                },
                () =>
                {

                });


            try
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.SendingHandshakeToWebsocketServer);



                //await WaitForHandShake();

                //await websocketSenderHandler.SendConnectHandShake(
                //        uri,
                //        origin,
                //        headers,
                //        _websocketParserHandler.SubprotocolAcceptedNames); 

                await Task.WhenAll(
                    WaitForHandShake(sender, setSenderAction),
                    websocketSenderHandler.SendConnectHandShake(
                        uri,
                        origin,
                        headers,
                        _websocketParserHandler.SubprotocolAcceptedNames));

                
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnError(ex);
            }
            finally
            {
                handshakeListnerDisposable.Dispose();
            }
        }


        private async Task<ParserState> WaitForHandShake(
            ISender sender,
            Action<ISender> setSenderAction)
        {
            var handShakeResult = await _websocketParserHandler
                .ParserDelegate
                .HandshakeParserCompletionObservable
                .Timeout(TimeSpan.FromSeconds(30))
                .Catch<ParserState, TimeoutException>(tx => Observable.Return(ParserState.HandshakeTimedOut));

            switch (handShakeResult)
            {
                case ParserState.HandshakeCompletedSuccessfully:
                    setSenderAction(sender);
                    _observerConnectionStatus.OnNext(ConnectionStatus.HandshakeCompletedSuccessfully);
                    break;
                case ParserState.HandshakeFailed:
                    throw new WebsocketClientLiteException("Unable to complete handshake");
                case ParserState.HandshakeTimedOut:
                    throw new WebsocketClientLiteException("Handshake timed out.");
                default:
                    throw new ArgumentOutOfRangeException($"Unknown parser state: {handShakeResult}");
            }

            return handShakeResult;
        }

        internal async Task DisconnectWebsocket(WebsocketSenderHandler sender)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Disconnecting);

            try
            {
                await sender.SendCloseHandshakeAsync(StatusCodes.GoingAway).ToObservable().Timeout(TimeSpan.FromSeconds(5));
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
            }
        }

        public void Dispose()
        {
            _websocketParserHandler.Dispose();
            _tcpConnectionService.Dispose();
        }
    }
}