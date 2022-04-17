using System;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Extension;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketConnectionHandler : IDisposable
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly WebsocketParserHandler _websocketParserHandler;
        private readonly Action<ConnectionStatus, Exception> _connectionStatusAction;
        private readonly Func<Stream, Action<ConnectionStatus, Exception>, WebsocketSenderHandler> _createWebsocketSenderFunc;

        private IDisposable _clientPingDisposable;

        internal WebsocketConnectionHandler(
            TcpConnectionService tcpConnectionService,
            WebsocketParserHandler websocketParserHandler,
            Action<ConnectionStatus, Exception> connectionStatusAction,
            Func<Stream, Action<ConnectionStatus, Exception>, WebsocketSenderHandler> createWebsocketSenderFunc)
        {
            _tcpConnectionService = tcpConnectionService;            
            _websocketParserHandler = websocketParserHandler;
            _connectionStatusAction = connectionStatusAction;
            _createWebsocketSenderFunc = createWebsocketSenderFunc;

            _clientPingDisposable = null;
        }

        internal async Task<IObservable<IDataframe>>
                ConnectWebsocket(
                    Uri uri,
                    X509CertificateCollection x509CertificateCollection,
                    SslProtocols tlsProtocolType,
                    Action<ISender> setSenderAction,
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

            if (tlsProtocolType is SslProtocols.None)
            {
                tlsProtocolType = SslProtocols.Tls12;
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
                await handshakeHandler.Handshake(uri, sender, timeout, ct, origin, headers, subprotocols);

            if(handshakeException is not null)
            {
                throw handshakeException;
            }
            else if (handshakeState is HandshakeStateKind.HandshakeCompletedSuccessfully)
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
                _clientPingDisposable = SendClientPing()
                    .Subscribe(
                    _ => { },
                    ex => 
                    { 
                        throw new WebsocketClientLiteException("Sending client ping failed.", ex); 
                    },
                    () => { });
            }

            _connectionStatusAction(ConnectionStatus.WebsocketConnected, null);

            return Observable.Create<IDataframe>(obs =>
            {

                var disposable = Observable.Defer(
                    () => _websocketParserHandler.DataframeObservable()
                    .Select(dataframe => Observable.FromAsync(ct => IncomingControlFrameHandler(dataframe, obs, ct)))
                    .Concat()
                    .Repeat()
                    .Where(dataframe => dataframe is not null)
                    .Do(dataframe => obs.OnNext(dataframe))
                )
                .FinallyAsync(async () =>
                {
                    await DisconnectWebsocket(sender);
                })
                .Subscribe();

                return disposable;

            })
            .Repeat()
            .FinallyAsync(async () =>
            {
                await DisconnectWebsocket(sender);
            });

            IObservable<Unit> SendClientPing() =>
                Observable.Interval(clientPingTimeSpan)
                .Select(_ => Observable.FromAsync(ct => sender.SendPing(default)))
                .Concat();

            async Task<Dataframe> IncomingControlFrameHandler(
                Dataframe dataframe, 
                IObserver<Dataframe> obs, 
                CancellationToken ct)
            {
                switch (dataframe.Opcode)
                {
                    case OpcodeKind.Continuation:
                    case OpcodeKind.Text:
                    case OpcodeKind.Binary:
                        return dataframe;
                    case OpcodeKind.Ping:
                        _connectionStatusAction(ConnectionStatus.PingReceived, null);
                        await sender.SendPong(dataframe, ct);
                        break;
                    case OpcodeKind.Pong:
                        _connectionStatusAction(ConnectionStatus.PongReceived, null);
                        break;
                    case OpcodeKind.Close:
                        _connectionStatusAction(ConnectionStatus.Close, null);
                        obs.OnCompleted();
                        break;
                    case OpcodeKind.Reserved1:
                    case OpcodeKind.Reserved2:
                    case OpcodeKind.Reserved3:
                    case OpcodeKind.Reserved4:
                    case OpcodeKind.Reserved5:
                    case OpcodeKind.Reserved1a:
                    case OpcodeKind.Reserved2b:
                    case OpcodeKind.Reserved3c:
                    case OpcodeKind.Reserved4d:
                    case OpcodeKind.Reserved5e:
                        throw new NotImplementedException($"Opcode not implemented: {dataframe.Opcode}");
                    default:
                        throw new ArgumentOutOfRangeException($"{dataframe.Opcode}");
                }

                return null;
            }
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