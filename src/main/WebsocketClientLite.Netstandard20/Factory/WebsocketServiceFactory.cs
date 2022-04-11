using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL.Factory
{
    internal class WebsocketServiceFactory
    {
        private WebsocketServiceFactory() 
        {

        }

        internal static async Task<WebsocketService> Create(
            Func<bool> isSecureConnectionSchemeFunc,
            Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
            EventLoopScheduler eventLoopScheduler,
            IObserver<ConnectionStatus> observerConnectionStatus,
            MessageWebsocketRx messageWebSocketRx)
        {           
            var controlFramHandler = new ControlFrameHandler(
                writeFunc: (stream, bytes, cts) => RunOnScheduler(WriteToStream(stream, bytes, cts),
                scheduler: eventLoopScheduler));

            var tcpConnectionHandler = new TcpConnectionService(
                            isSecureConnectionSchemeFunc,
                            validateServerCertificateFunc,
                            ConnectTcpClient,
                            ReadOneByteFromStream,
                            messageWebSocketRx.TcpClient);

            var dataReceiveSubject = new Subject<DataReceiveState>();

            var websocketServices = new WebsocketService(
                new WebsocketConnectionHandler(
                        tcpConnectionHandler,
                        new WebsocketParserHandler(
                            tcpConnectionHandler,
                            messageWebSocketRx.SubprotocolAccepted,
                            messageWebSocketRx.ExcludeZeroApplicationDataInPong,
                            ReadOneByteFromStream,
                            controlFramHandler,
                            ConnectionStatusAction),
                        controlFramHandler,
                        observerConnectionStatus,
                        ConnectionStatusAction,
                        (stream, observerConnectionStatus) => new WebsocketSenderHandler(
                            observerConnectionStatus,
                            stream,
                            (stream, bytes, cts) => RunOnScheduler(WriteToStream(stream, bytes, cts), eventLoopScheduler))));
            

            await Task.CompletedTask;

            return websocketServices;
       
            void ConnectionStatusAction(ConnectionStatus status)
            {
                observerConnectionStatus.OnNext(status);
            }

            async Task<bool> WriteToStream(Stream stream, byte[] b, CancellationToken ct)
            {
                await stream.WriteAsync(b, 0, b.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);

                return true;
            }

            async Task<int> ReadOneByteFromStream(Stream stream, byte[] b, CancellationToken ct) 
                => await stream.ReadAsync(b, 0, 1, ct).ConfigureAwait(false);

            async Task ConnectTcpClient(TcpClient tcpClient, Uri uri) 
                => await tcpClient
                    .ConnectAsync(uri.Host, uri.Port)
                    .ConfigureAwait(false);

            // Running sends and writes on the Event Loop Scheduler serializes them.
            async Task<T> RunOnScheduler<T>(Task<T> task, IScheduler scheduler) 
                => await task.ToObservable().ObserveOn(scheduler);
        }
    }
}
