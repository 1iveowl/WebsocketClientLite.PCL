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
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Service;

namespace WebsocketClientLite.Factory;

internal class WebsocketClientFactory
{
    private WebsocketClientFactory() { }

    internal static async Task<WebsocketService> Create(
        Func<bool> isSecureConnectionSchemeFunc,
        Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
        EventLoopScheduler eventLoopScheduler,
        IObserver<ConnectionStatus> observerConnectionStatus,
        ClientWebSocketRx webSocketClientRx)
    {
        var tcpConnectionHandler = new TcpConnectionService(
            isSecureConnectionSchemeFunc: isSecureConnectionSchemeFunc,
            validateServerCertificateFunc: validateServerCertificateFunc,
            connectTcpClientFunc: ConnectTcpClient,
            connectionStatusAction: ConnectionStatusAction,
            webSocketClientRx.HasTransferSocketLifeCycleOwnership,
            tcpClient: webSocketClientRx.TcpClient);

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
                            (stream, bytes, cts) => RunOnScheduler(WriteToStream(stream, bytes, cts), eventLoopScheduler),
                            webSocketClientRx.ExcludeZeroApplicationDataInPong
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
            await stream.WriteAsync(byteArray, 0, byteArray.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);

            return true;
        }

        async Task ConnectTcpClient(TcpClient tcpClient, Uri uri) 
            => await tcpClient
                .ConnectAsync(uri.Host, uri.Port)
                .ConfigureAwait(false);

        // Running sends and/or writes on the Event Loop Scheduler serializes them one-by-one.
        async Task<T> RunOnScheduler<T>(Task<T> task, IScheduler scheduler) 
            => await task.ToObservable().ObserveOn(scheduler);
    }
}
