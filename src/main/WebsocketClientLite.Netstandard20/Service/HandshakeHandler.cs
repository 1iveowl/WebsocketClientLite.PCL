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
using System.Reactive.Concurrency;
using System.Reactive.Disposables;

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

        internal IObservable<(HandshakeState handshakeState, WebsocketClientLiteException ex)> Handshake(
            Uri uri,
            WebsocketSenderHandler sender,
            CancellationToken ct,
            EventLoopScheduler eventLoopScheduler,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            return Observable.Create<(HandshakeState handshakeState, WebsocketClientLiteException ex)>(async obs =>
            {
                using var parserDelegate = new WebsocketHandshakeParserDelegate(obs);
                using var parserHandler = new HttpCombinedParser(parserDelegate);

                var handshakeParser = new HandshakeParser(
                    parserHandler,
                    parserDelegate,
                    _connectionStatusAction);

                await SendHandshake(uri, sender, ct, origin, headers);

                //obs.OnNext((HandshakeState.HandshakeFailed, null));

                //var result = Task.Run(() => SendHandshake(uri, sender, ct, origin, headers)).Result;

                //obs.OnNext(result);

                //var cts = new CancellationTokenSource();

                //do
                //{
                //    var byteArray = await _tcpConnectionService.ReadByteArrayFromStream(1, cts.Token);

                //    var result = handshakeParser.Parse(byteArray, subprotocols);

                //    if (result == HandshakeStateKind.IsListening)
                //    {
                //        cts.Cancel();
                //    }

                //} while (!cts.IsCancellationRequested);

                //obs.OnCompleted();

                //return Disposable.Empty;

                return _tcpConnectionService.BytesObservable()
                    //.Repeat()
                    .Select(b => handshakeParser.Parse(b, subprotocols))
                    .Repeat()
                    //.TakeUntil(d => d == HandshakeStateKind.IsListening)
                    .Where(d => d == HandshakeStateKind.IsListening)
                    //.TakeWhile(d => d == HandshakeStateKind.IsListeningForHandShake)
                    //.ObserveOn(eventLoopScheduler)
                    .Subscribe(
                    _ => { obs.OnCompleted(); },
                    ex => { obs.OnNext((HandshakeState.HandshakeFailed, new WebsocketClientLiteException("Unknown error", ex))); },
                    () => { });

                //return disposable;
            })
            .Timeout(TimeSpan.FromSeconds(30))
            .Catch<
                (HandshakeState handshakeState, WebsocketClientLiteException ex),
                TimeoutException>(
                    tx => Observable.Return(
                        (HandshakeState.HandshakeTimedOut,
                        new WebsocketClientLiteException("Handshake times out.", tx))
                    )
                );
        }

        private async Task<(HandshakeState handshakeState, WebsocketClientLiteException ex)> 
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
                    HandshakeState.HandshakeSendFailed, 
                    new WebsocketClientLiteException("Handshake send failed.", ex)
                );
            }

            return (HandshakeState.HandshakeSend, null);
        }
    }
}
