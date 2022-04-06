using HttpMachine;
using IWebsocketClientLite.PCL;
using System;
using System.IO;
using System.Net.Security;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL.Factory
{
    internal class WebsocketService : IDisposable
    {
        internal WebsocketConnectionHandler WebsocketConnectHandler { get; private set; }

        private WebsocketService()
        {

        }

        internal static async Task<WebsocketService> Create(
            Func<bool> isSecureConnectionSchemeFunc,
            Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
            IObserver<ConnectionStatus> observerConnectionStatus,
            MessageWebSocketRx messageWebSocketRx)
        {
            var websocketServices = new WebsocketService
            {
                WebsocketConnectHandler =
                    new WebsocketConnectionHandler(
                        new TcpConnectionService(
                            isSecureConnectionSchemeFunc,
                            validateServerCertificateFunc,
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
                await stream.WriteAsync(b, 0, b.Length);
                await stream.FlushAsync();
            }
        }

        public void Dispose()
        {
            WebsocketConnectHandler.Dispose();         
        }
    }
}
