using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectService
    {
        internal Stream TcpStream;

        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;

        internal WebSocketConnectService(IObserver<ConnectionStatus> observerConnectionStatus)
        {
            _observerConnectionStatus = observerConnectionStatus;
        }

        internal async Task ConnectServer(
            Uri uri,
            bool secure,
            CancellationToken token,
            Stream tcpStream,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            TcpStream = tcpStream;
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
                await TcpStream.WriteAsync(handShake, 0, handShake.Length, token);
                await TcpStream.FlushAsync(token);
            }
            catch (System.Exception ex)
            {
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }
    }
}
