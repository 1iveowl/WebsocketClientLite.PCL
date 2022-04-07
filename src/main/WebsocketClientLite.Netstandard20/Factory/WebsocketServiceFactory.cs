using HttpMachine;
using IWebsocketClientLite.PCL;
using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL.Factory
{
    internal class WebsocketServiceFactory : IDisposable
    {
        internal WebsocketConnectionHandler WebsocketConnectHandler { get; private set; }

        private WebsocketServiceFactory()
        {

        }

        internal static async Task<WebsocketServiceFactory> Create(
            Func<bool> isSecureConnectionSchemeFunc,
            Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
            IObserver<ConnectionStatus> observerConnectionStatus,
            MessageWebSocketRx messageWebSocketRx)
        {
            var websocketServices = new WebsocketServiceFactory
            {
                WebsocketConnectHandler =
                    new WebsocketConnectionHandler(
                        new TcpConnectionService(
                            isSecureConnectionSchemeFunc,
                            validateServerCertificateFunc,
                            ConnectTcpClient,
                            messageWebSocketRx.TcpClient),
                        new WebsocketParserHandler(
                            messageWebSocketRx.SubprotocolAccepted,
                            messageWebSocketRx.ExcludeZeroApplicationDataInPong,
                            new HttpWebSocketParserDelegate(),
                            observerConnectionStatus),
                        observerConnectionStatus,
                        (stream, observerConnectionStatus) => new WebsocketSenderHandler(
                            observerConnectionStatus,
                            stream,
                            WriteStream))
            };

            await Task.CompletedTask;

            return websocketServices;

            static async Task WriteStream(byte[] b, Stream stream)
            {
                await stream.WriteAsync(b, 0, b.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            static async Task ConnectTcpClient(TcpClient tcpClient, Uri uri)
            {
                await tcpClient
                    .ConnectAsync(uri.Host, uri.Port)
                    .ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            WebsocketConnectHandler.Dispose();         
        }
    }
}
