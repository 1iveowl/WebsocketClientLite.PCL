using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL.Factory
{
    internal class WebsocketServiceFactory
    {
        private WebsocketServiceFactory() { }

        internal static async Task<WebsocketService> Create(
            Func<bool> isSecureConnectionSchemeFunc,
            Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
            EventLoopScheduler eventLoopScheduler,
            IObserver<ConnectionStatus> observerConnectionStatus,
            MessageWebsocketRx messageWebSocketRx)
        {
            var sendSem = new SemaphoreSlim(1, 1);

            var tcpConnectionHandler = new TcpConnectionService(
                isSecureConnectionSchemeFunc: isSecureConnectionSchemeFunc,
                validateServerCertificateFunc: validateServerCertificateFunc,
                connectTcpClientFunc: ConnectTcpClient,
                ReadOneByteFromStream,
                //readOneByteFunc: (stream, bytes, cts) => RunOnScheduler(ReadOneByteFromStream(stream, bytes, cts), eventLoopScheduler),
                connectionStatusAction: ConnectionStatusAction,
                messageWebSocketRx.HasTransferSocketLifeCycleOwnership,
                tcpClient: messageWebSocketRx.TcpClient);

            var websocketServices = new WebsocketService(
                new WebsocketConnectionHandler(
                        tcpConnectionHandler,
                        new WebsocketParserHandler(
                            tcpConnectionHandler),
                        ConnectionStatusAction,
                        (stream, connectionStatusAction) => 
                            new WebsocketSenderHandler(
                                tcpConnectionHandler,
                                ConnectionStatusAction,
                                //WriteToStream
                                (stream, bytes, cts) => RunOnScheduler(WriteToStream(stream, bytes, cts), eventLoopScheduler),
                                messageWebSocketRx.ExcludeZeroApplicationDataInPong
                            )
                        )                        
                );
            
            await Task.CompletedTask;

            return websocketServices;
       
            void ConnectionStatusAction(ConnectionStatus status, Exception? ex)
            {
                if (status is ConnectionStatus.Disconnected)
                {
                    observerConnectionStatus.OnCompleted();
                }

                if (status is ConnectionStatus.Aborted)
                {
                    observerConnectionStatus.OnError(
                        ex ?? new WebsocketClientLiteException("Unknown error."));
                }
                observerConnectionStatus.OnNext(status);
            }

            async Task<bool> WriteToStream(Stream stream, byte[] byteArray, CancellationToken ct)
            {
                try
                {
                    await sendSem.WaitAsync(ct);
                    await stream.WriteAsync(byteArray, 0, byteArray.Length, ct).ConfigureAwait(false);
                }
                finally
                {
                    sendSem.Release();
                }
                
                await stream.FlushAsync().ConfigureAwait(false);

                return true;
            }

            async Task<int> ReadOneByteFromStream(Stream stream, byte[] byteArray, CancellationToken ct)
            {
                try
                {
                    return await stream.ReadAsync(byteArray, 0, byteArray.Length, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return -1;
                }
                
            }

            async Task ConnectTcpClient(TcpClient tcpClient, Uri uri) 
                => await tcpClient
                    .ConnectAsync(uri.Host, uri.Port)
                    .ConfigureAwait(false);

            // Running sends and/or writes on the Event Loop Scheduler serializes them.
            async Task<T> RunOnScheduler<T>(Task<T> task, IScheduler scheduler) 
                => await task.ToObservable().ObserveOn(scheduler);
        }
    }
}
