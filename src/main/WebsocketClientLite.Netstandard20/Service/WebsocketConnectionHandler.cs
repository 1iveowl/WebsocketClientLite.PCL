using System;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Extension;
using System.Reactive.Disposables;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketConnectionHandler : IDisposable
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly WebsocketParserHandler _websocketParserHandler;
        private readonly ControlFrameHandler _controlFrameHandler;
        private readonly Action<ConnectionStatus, Exception> _connectionStatusAction;
        private readonly Func<Stream, Action<ConnectionStatus, Exception>, WebsocketSenderHandler> _createWebsocketSenderFunc;

        private IDisposable _clientPingDisposable;

        internal WebsocketConnectionHandler(
            TcpConnectionService tcpConnectionService,
            WebsocketParserHandler websocketParserHandler,
            ControlFrameHandler controlFrameHandler,
            Action<ConnectionStatus, Exception> connectionStatusAction,
            Func<Stream, Action<ConnectionStatus, Exception>, WebsocketSenderHandler> createWebsocketSenderFunc)
        {
            _tcpConnectionService = tcpConnectionService;            
            _websocketParserHandler = websocketParserHandler;
            _controlFrameHandler = controlFrameHandler;
            _connectionStatusAction = connectionStatusAction;
            _createWebsocketSenderFunc = createWebsocketSenderFunc;

            _clientPingDisposable = null;
        }

        internal async Task<IObservable<IDatagram>>
                ConnectWebsocket(
                    Uri uri,
                    X509CertificateCollection x509CertificateCollection,
                    SslProtocols tlsProtocolType,
                    Action<ISender> setSenderAction,
                    EventLoopScheduler eventLoopScheduler,
                    CancellationToken ct,
                    bool hasClientPing = false,
                    TimeSpan clientPingTimeSpan = default,
                    TimeSpan timeout = default,
                    string origin = null,
                    IDictionary<string, string> headers = null,
                    IEnumerable<string> subprotocols = null)
        {
            if (hasClientPing && clientPingTimeSpan == default)
            {
                clientPingTimeSpan = TimeSpan.FromSeconds(30);
            }

            await _tcpConnectionService.ConnectTcpStream(
                uri,
                x509CertificateCollection,
                tlsProtocolType,
                timeout);

            var sender = _createWebsocketSenderFunc(
                _tcpConnectionService.ConnectionStream,
                _connectionStatusAction);

            var handshakeHandler = new HandshakeHandler(
                    _tcpConnectionService,
                    _websocketParserHandler,
                    _connectionStatusAction);

            var (handshakeState, handshakeException) = 
                await handshakeHandler.Handshake(uri, sender, ct, eventLoopScheduler, origin, headers, subprotocols);

            if(handshakeException is not null)
            {
                throw handshakeException;
            }
            else if (handshakeState == HandshakeState.HandshakeCompletedSuccessfully)
            {
                _connectionStatusAction(ConnectionStatus.HandshakeCompletedSuccessfully, null);              
            }
            else
            {
                throw new WebsocketClientLiteException($"Handshake failed due to unknown error: {handshakeState}");
            }

            setSenderAction(sender);

            if (hasClientPing)
            {
                _clientPingDisposable = ClientPing()
                    .Subscribe(
                    _ => { },
                    ex => 
                    { 
                        throw new WebsocketClientLiteException("Sending client ping failed.", ex); 
                    },
                    () => { });
            }

            _connectionStatusAction(ConnectionStatus.WebsocketConnected, null);

            //return Observable.Create<IDatagram>(async obs =>
            //{

            //    var datagram = await _websocketParserHandler.DatagramObservable();

            //    obs.OnNext(datagram);

            //    //if (datagram.Opcode == OpcodeKind.)

            //    return Disposable.Empty;

            //})
            //.Repeat()
            //.FinallyAsync(async () =>
            //{
            //    await DisconnectWebsocket(sender);
            //});

            return _websocketParserHandler
                .DatagramObservable()

                //.Do(_ => _connectionStatusAction(ConnectionStatus.WebsocketConnected, null))
                //.ObserveOn(eventLoopScheduler)
                //.Select(datagram => datagram.Message)
                .Repeat()            
                .FinallyAsync(async () =>
                {
                    await DisconnectWebsocket(sender);
                });


            IObservable<Unit> ClientPing() =>
                Observable.Interval(clientPingTimeSpan)
                            .Select(_ => Observable.FromAsync(ct => 
                                _controlFrameHandler.SendPing(_tcpConnectionService.ConnectionStream, ct)))
                            .Concat();
        }

        internal async Task DisconnectWebsocket(
            WebsocketSenderHandler sender)
        {
            try
            {
                await sender.SendCloseHandshakeAsync(StatusCodes.GoingAway)
                    .ToObservable()
                    .Timeout(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                throw new WebsocketClientLiteException("Unable to disconnect gracefully", ex);
            }
            finally
            {
                _connectionStatusAction(ConnectionStatus.Disconnected, null);
            }
        }

        public void Dispose()
        {
            if(_clientPingDisposable is not null)
            {
                _clientPingDisposable.Dispose();
            }

            _websocketParserHandler.Dispose();
            _tcpConnectionService.Dispose();
        }
    }
}