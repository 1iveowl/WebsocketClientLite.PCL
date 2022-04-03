using IWebsocketClientLite.PCL;
using System;
using System.Net.Security;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL.Factory
{
    internal class WebsocketServices : IDisposable
    {
        internal IObservable<ConnectionStatus> ConnectionStatusObservable;
        internal IObservable<string> MessageReceiverObservable;
        internal WebsocketConnectionHandler WebsocketConnectHandler { get; private set; }

        private WebsocketServices()
        {

        }

        internal static async Task<WebsocketServices> Create(
            Func<bool> isSecureConnectionSchemeFunc,
            Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
            MessageWebSocketRx messageWebSocketRx)
        {
            var subjectConnectionStatus = new BehaviorSubject<ConnectionStatus>(ConnectionStatus.Initialized);
            var subjectMessageReceive = new Subject<string>();

            var observerConnectionStatus = subjectConnectionStatus.AsObserver();
            var observerMessageReceive = subjectMessageReceive.AsObserver();

            var websocketServices = new WebsocketServices
            {
                ConnectionStatusObservable = subjectConnectionStatus.AsObservable(),
                MessageReceiverObservable = subjectMessageReceive.AsObservable(),
                WebsocketConnectHandler = new WebsocketConnectionHandler(
                                            new TcpConnectionService(
                                                isSecureConnectionSchemeFunc,
                                                validateServerCertificateFunc,
                                                messageWebSocketRx.TcpClient),
                                            new WebsocketParserHandler(
                                                messageWebSocketRx.SubprotocolAccepted,
                                                messageWebSocketRx.ExcludeZeroApplicationDataInPong,
                                                observerConnectionStatus),
                                            observerConnectionStatus,
                                            observerMessageReceive)
            };

            await Task.CompletedTask;

            return websocketServices;
        }

        public void Dispose()
        {
            WebsocketConnectHandler.Dispose();         
        }
    }
}
