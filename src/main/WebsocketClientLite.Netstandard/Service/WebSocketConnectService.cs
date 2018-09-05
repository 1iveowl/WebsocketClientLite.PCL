using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectService
    {
        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;

        internal bool IsConnected { get; private set; }
        internal bool IsHandshakeDone { get; private set; }

        internal WebSocketConnectService(IObserver<ConnectionStatus> observerConnectionStatus)
        {
            _observerConnectionStatus = observerConnectionStatus;
        }

        internal async Task<Stream> ConnectWebsocketServerAsync(
            Uri uri,
            bool secure,
            CancellationToken token,
            Stream tcpStream,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            _observerConnectionStatus.OnNext(ConnectionStatus.Connecting);

            await SendConnectHandShakeAsync(uri, secure, tcpStream, token, origin, headers, subprotocols);

            return tcpStream;

        }

        private async Task SendConnectHandShakeAsync(
            Uri uri, 
            bool secure,
            Stream tcpStream,
            CancellationToken token,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocol = null
            )
        {
            IsConnected = false;
            
            var handShake = ClientHandShake.Compose(uri, secure, origin, headers, subprotocol);
            try
            {
                await tcpStream.WriteAsync(handShake, 0, handShake.Length, token);
                await tcpStream.FlushAsync(token);
                IsHandshakeDone = true;
                IsConnected = true;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                _observerConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }
    }
}
