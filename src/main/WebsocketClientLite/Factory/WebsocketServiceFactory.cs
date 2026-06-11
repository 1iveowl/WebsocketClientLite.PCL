using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.PCL; // Obsolete legacy client
using WebsocketClientLite.Service;

namespace WebsocketClientLite.Factory;

internal static class WebsocketServiceFactory
{
    internal static Task<WebsocketService> Create(
        Func<bool> isSecureConnectionSchemeFunc,
        Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
        IObserver<ConnectionStatus> observerConnectionStatus,
#pragma warning disable CS0618
        MessageWebsocketRx messageWebSocketRx)
#pragma warning restore CS0618
    {
        // Construct disposables explicitly
        var tcpConnectionHandler = new TcpConnectionService(
            isSecureConnectionSchemeFunc: isSecureConnectionSchemeFunc,
            validateServerCertificateFunc: validateServerCertificateFunc,
            connectTcpClientFunc: ConnectTcpClient,
            connectionStatusAction: ConnectionStatusAction,
#pragma warning disable CS0618
            hasTransferTcpSocketLifeCycleOwnership: messageWebSocketRx.HasTransferSocketLifeCycleOwnership,
            tcpClient: messageWebSocketRx.TcpClient,
            maxFrameSize: messageWebSocketRx.MaxFrameSize,
            checkCertificateRevocation: messageWebSocketRx.CheckCertificateRevocation);
#pragma warning restore CS0618

        var parserHandler = new WebsocketParserHandler(tcpConnectionHandler);

        var connectionHandler = new WebsocketConnectionHandler(
            tcpConnectionHandler,
            parserHandler,
            ConnectionStatusAction,
            (stream, connectionStatusAction) =>
#pragma warning disable CS0618
                new WebsocketSenderHandler(
                    tcpConnectionHandler,
                    ConnectionStatusAction,
                    WriteToStream,
                    messageWebSocketRx.ExcludeZeroApplicationDataInPong));
#pragma warning restore CS0618

        // Pass all disposables to WebsocketService so ownership is explicit (mirrors modern factory).
        var service = new WebsocketService(
            tcpConnectionHandler,
            parserHandler,
            connectionHandler);

        return Task.FromResult(service);

        void ConnectionStatusAction(ConnectionStatus status, Exception? ex)
        {
            // Terminal Rx events must come last: emit the status first, then
            // complete/error (mirrors WebsocketClientFactory).
            if (status is ConnectionStatus.Aborted)
            {
                observerConnectionStatus.OnError(
                    ex ?? new WebsocketClientLiteException("Unknown error."));
                return;
            }

            observerConnectionStatus.OnNext(status);

            if (status is ConnectionStatus.Disconnected)
            {
                observerConnectionStatus.OnCompleted();
            }
        }

        async Task<bool> WriteToStream(Stream stream, byte[] byteArray, int count, CancellationToken ct)
        {
#if NETSTANDARD2_0
            await stream.WriteAsync(byteArray, 0, count, ct).ConfigureAwait(false);
#else
            await stream.WriteAsync(byteArray.AsMemory(0, count), ct).ConfigureAwait(false);
#endif
            await stream.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }

        async Task ConnectTcpClient(TcpClient tcpClient, Uri uri) =>
            await tcpClient
                .ConnectAsync(uri.Host, uri.Port)
                .ConfigureAwait(false);
    }
}
