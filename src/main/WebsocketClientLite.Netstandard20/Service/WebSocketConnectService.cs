using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectService
    {

        internal Stream tcpStream;

        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;

        internal WebSocketConnectService(IObserver<ConnectionStatus> observerConnectionStatus)
        {
            _observerConnectionStatus = observerConnectionStatus;
        }

        internal async Task ConnectServer(
            Uri uri,
            bool secure,
            CancellationToken token,
            Stream tcpSocketClient,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            tcpStream = tcpSocketClient;
            _observerConnectionStatus.OnNext(ConnectionStatus.Connecting);

            await SendConnectHandShakeAsync(uri, secure, token, origin, headers, subprotocols);

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
                await tcpStream.WriteAsync(handShake, 0, handShake.Length, token);
                await tcpStream.FlushAsync(token);
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }
    }
}
