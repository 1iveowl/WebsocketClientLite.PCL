using System;
using HttpMachine;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class HandshakeHandler
    {
        private readonly TcpConnectionService _tcpConnectionService;
        private readonly WebsocketParserHandler _websocketParserHandler;
        private readonly Action<ConnectionStatus, Exception> _connectionStatusAction;

        public HandshakeHandler(
            TcpConnectionService tcpConnectionService,
            WebsocketParserHandler websocketParserHandler,
            Action<ConnectionStatus, Exception> connectionStatusAction)
        {
            _tcpConnectionService = tcpConnectionService;
            _websocketParserHandler = websocketParserHandler;
            _connectionStatusAction = connectionStatusAction;
        }

        internal IObservable<(HandshakeStateKind handshakeState, WebsocketClientLiteException ex)> Handshake(
            Uri uri,
            WebsocketSenderHandler sender,
            CancellationToken ct,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            return Observable.Create<(HandshakeStateKind handshakeState, WebsocketClientLiteException ex)>(async obs =>
            {
                using var parserDelegate = new WebsocketHandshakeParserDelegate(obs);
                using var parserHandler = new HttpCombinedParser(parserDelegate);

                var handshakeParser = new HandshakeParser(
                    parserHandler,
                    parserDelegate,
                    _connectionStatusAction);

                await SendHandshake(uri, sender, ct, origin, headers);

                return _tcpConnectionService.BytesObservable()
                    .Select(b => handshakeParser.Parse(b, subprotocols))
                    .Repeat()
                    .Where(d => d == HandshakeStateKind.HandshakeCompletedSuccessfully)
                    .Subscribe(
                    _ => { obs.OnCompleted(); },
                    ex => { obs.OnNext((HandshakeStateKind.HandshakeFailed, new WebsocketClientLiteException("Unknown error", ex))); },
                    () => { });
            })
            .Timeout(TimeSpan.FromSeconds(30))
            .Catch<
                (HandshakeStateKind handshakeState, WebsocketClientLiteException ex),
                TimeoutException>(
                    tx => Observable.Return(
                        (HandshakeStateKind.HandshakeTimedOut,
                        new WebsocketClientLiteException("Handshake times out.", tx))
                    )
                );
        }

        private async Task<(HandshakeStateKind handshakeState, WebsocketClientLiteException ex)> 
            SendHandshake(
                Uri uri,
                WebsocketSenderHandler websocketSenderHandler,
                CancellationToken ct,
                string origin = null,
                IDictionary<string, string> headers = null)
        {
            try
            {
                _connectionStatusAction(ConnectionStatus.SendingHandshakeToWebsocketServer, null);

                await websocketSenderHandler.SendConnectHandShake(
                         uri,
                         ct,
                         origin,
                         headers,
                         _websocketParserHandler.SubprotocolAcceptedNames);     
            }
            catch (Exception ex)
            {
                return (
                    HandshakeStateKind.HandshakeSendFailed, 
                    new WebsocketClientLiteException("Handshake send failed.", ex)
                );
            }

            return (HandshakeStateKind.HandshakeSend, null);
        }
    }
}
