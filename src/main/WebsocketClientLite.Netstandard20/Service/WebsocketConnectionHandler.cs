using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Helper;
using System.Reactive;
using HttpMachine;
using WebsocketClientLite.PCL.Extension;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketConnectionHandler : IDisposable
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly WebsocketParserHandler _websocketParserHandler;
        private readonly ControlFrameHandler _controlFrameHandler;
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;
        private readonly Action<ConnectionStatus> _connectionStatusAction;
        private readonly Func<Stream, IObserver<ConnectionStatus>, WebsocketSenderHandler> _createWebsocketSenderFunc;

        internal WebsocketConnectionHandler(
            TcpConnectionService tcpConnectionService,
            WebsocketParserHandler websocketParserHandler,
            ControlFrameHandler controlFrameHandler,
            IObserver<ConnectionStatus> observerConnectionStatus,
            Action<ConnectionStatus> connectionStatusAction,
            Func<Stream, IObserver<ConnectionStatus>, WebsocketSenderHandler> createWebsocketSenderFunc)
        {
            _tcpConnectionService = tcpConnectionService;            
            _websocketParserHandler = websocketParserHandler;
            _controlFrameHandler = controlFrameHandler;
            _observerConnectionStatus = observerConnectionStatus;
            _connectionStatusAction = connectionStatusAction;
            _createWebsocketSenderFunc = createWebsocketSenderFunc;
        }

        internal async Task<IObservable<string>>
                ConnectWebsocket(
                    Uri uri,
                    X509CertificateCollection x509CertificateCollection,
                    SslProtocols tlsProtocolType,
                    Action<ISender> setSenderAction,                    
                    CancellationToken ct,
                    bool hasClientPing = false,
                    TimeSpan clientPingTimeSpan = default,
                    EventLoopScheduler eventLoopScheduler = default,
                    TimeSpan timeout = default,
                    string origin = null,
                    IDictionary<string, string> headers = null,
                    IEnumerable<string> subprotocols = null)
        {
            if (hasClientPing && clientPingTimeSpan == default)
            {
                clientPingTimeSpan = TimeSpan.FromSeconds(30);
            }

            var stream = await _tcpConnectionService.ConnectTcpClientAndStream(
                uri, 
                () => _observerConnectionStatus.OnNext(ConnectionStatus.TcpSocketConnected),
                x509CertificateCollection,
                tlsProtocolType,
                timeout);

            var sender = _createWebsocketSenderFunc(stream, _observerConnectionStatus);

            var handshakeHandler = new HandshakeHandler(
                    _tcpConnectionService,
                    _websocketParserHandler,
                    _connectionStatusAction);

            var (handshakeState, ex) = 
                await handshakeHandler.Connect(uri, sender, ct, origin, headers, subprotocols);

            switch (handshakeState)
            {
                case HandshakeState.HandshakeCompletedSuccessfully:
                    _connectionStatusAction(ConnectionStatus.HandshakeCompletedSuccessfully);
                    break;
                case HandshakeState.HandshakeFailed:
                    throw new WebsocketClientLiteException("Unable to complete handshake");
                case HandshakeState.HandshakeTimedOut:
                    throw new WebsocketClientLiteException("Handshake timed out.");
                default:
                    throw new ArgumentOutOfRangeException($"Unknown parser state: {handshakeState}");
            }

            //var x = await u;

            //var x = await Observable.TakeWhile(u.Repeat(), c => c.);



            //var t = await _tcpConnectionService.ByteStreamObservable()
            //    .Select(b => handshakeParser.Parse(b, subprotocols))  
            //    .TakeUntil(b => b != DataReceiveState.IsListeningForHandShake);


            //var t = Observable.Create(obs =>
            //{
            //    using var parserHandler = new HttpCombinedParser(ParserDelegate);

            //    var handshakeParser = new HandshakeParser(
            //        parserHandler,
            //        ParserDelegate,
            //        _connectionStatusAction);
            //});

            //var listener = _websocketParserHandler
            //    .CreateWebsocketListenerObservable(
            //        stream,
            //        c => HandShake(c),
            //        c => WaitHandShake(c),
            //        subprotocols,
            //        eventLoopScheduler,
            //        hasClientPing ? ClientPing() : default)
            //    // https://stackoverflow.com/a/45217578/4140832
            //    .FinallyAsync(async () =>
            //    {
            //        await DisconnectWebsocket(sender, ct);
            //    });

            setSenderAction(sender);

            var listener = _websocketParserHandler.CreateWebsocketListenerObservable(
                stream,
                subprotocols,
                eventLoopScheduler,                
                //ClientPing, 
                false)
                .FinallyAsync(async () =>
                {
                    await DisconnectWebsocket(sender, ct);
                });

            return listener.Select(tuple => tuple.message);

            IObservable<Unit> ClientPing() =>
                Observable.Interval(clientPingTimeSpan)
                            .ObserveOn(eventLoopScheduler)
                            .Select(_ => Observable.FromAsync(ct => _controlFrameHandler.SendPing(stream, ct)))
                            .Concat();
        }

        internal async Task DisconnectWebsocket(
            WebsocketSenderHandler sender,
            CancellationToken ct)
        {
            try
            {
                await sender.SendCloseHandshakeAsync(StatusCodes.GoingAway, ct)
                    .ToObservable()
                    .Timeout(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                throw new WebsocketClientLiteException("Unable to disconnect.", ex);
            }

            try
            {
                //await _websocketParserHandler.DataReceiveStateObservable
                //    .Timeout(TimeSpan.FromSeconds(10));
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