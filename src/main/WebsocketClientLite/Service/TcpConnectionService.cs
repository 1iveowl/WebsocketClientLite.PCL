using IWebsocketClientLite;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.CustomException;

namespace WebsocketClientLite.Service;

internal class TcpConnectionService(
    Func<bool> isSecureConnectionSchemeFunc,
    Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
    Func<TcpClient, Uri, Task> connectTcpClientFunc,
    Func<Stream, byte[], CancellationToken, Task<int>> readOneByteFunc,
    Action<ConnectionStatus, Exception?> connectionStatusAction,
    bool hasTransferTcpSocketLifeCycleOwnership,
    TcpClient? tcpClient = null) : IDisposable
{
    private readonly bool _keepTcpClientAlive = !hasTransferTcpSocketLifeCycleOwnership;
    private Stream? _stream;

    internal Stream ConnectionStream => _stream ?? throw new NullReferenceException("Stream cannot be null");

    internal virtual async Task ConnectTcpStream(
        Uri uri,
        X509CertificateCollection? x509CertificateCollection,
        SslProtocols tlsProtocolType,
        TimeSpan timeout = default)
    {
        await ConnectTcpClient(uri, timeout);

        _stream = await GetTcpStream(uri, tcpClient, x509CertificateCollection, tlsProtocolType);
    }

    internal IObservable<byte[]?> BytesObservable() =>
        Observable.Defer(() => Observable.FromAsync(ct => ReadByteArrayFromStream(1, ct)))
        .Where(bytes => bytes is not null);

    internal async Task<byte[]?> ReadBytesFromStream(ulong size, CancellationToken ct) =>
        await ReadByteArrayFromStream(size, ct);

    internal async Task<byte[]?> ReadByteArrayFromStream(ulong size, CancellationToken ct)
    {          
        if (_stream is null || !_stream.CanRead)
        {
            throw new WebsocketClientLiteException("Stream not ready or not connected.");
        }

        var byteArray = new byte[size];

        try
        {
            if (_stream is null)
            {
                throw new WebsocketClientLiteException("Read stream cannot be null.");
            }

            if (!_stream.CanRead)
            {
                throw new WebsocketClientLiteException("Websocket connection have been closed.");
            }

            var readLength = await readOneByteFunc(_stream, byteArray, ct);

            if (readLength == 0)
            {
                throw new WebsocketClientLiteException("Websocket connection aborted unexpectedly. Check connection and socket security version/TLS version).");
            }

            if (readLength == -1)
            {
                return null;
            }
        }
        catch (ObjectDisposedException)
        {
            Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
        }
        return byteArray;
    }

    private async Task ConnectTcpClient(
        Uri uri,
        TimeSpan timeout = default)
    {
        connectionStatusAction(ConnectionStatus.ConnectingToTcpSocket, null);

        tcpClient ??= new TcpClient(
                uri.HostNameType is UriHostNameType.IPv6
                    ? AddressFamily.InterNetworkV6
                    : AddressFamily.InterNetwork);

        try
        {
            if (!tcpClient.Connected)
            {
                await connectTcpClientFunc(tcpClient, uri)
                    .ToObservable()
                    .Timeout(timeout != default ? timeout : TimeSpan.FromSeconds(15));
            }
        }
        catch (TimeoutException ex)
        {
            throw new WebsocketClientLiteTcpConnectException($"TCP Socket connection timed-out to {uri.Host}:{uri.Port}.", ex);
        }
        catch (ObjectDisposedException)
        {
            // OK to ignore
        }
        catch (Exception ex)
        {
            throw new WebsocketClientLiteTcpConnectException($"Unable to establish TCP Socket connection to: {uri.Host}:{uri.Port}.", ex);
        }

        if (tcpClient.Connected)
        {
            connectionStatusAction(ConnectionStatus.TcpSocketConnected, null);
            Debug.WriteLine("Connected");
        }
        else
        {
            throw new WebsocketClientLiteTcpConnectException($"Unable to connect to Tcp socket for: {uri.Host}:{uri.Port}.");
        }
    }

    private async Task<Stream> GetTcpStream(
        Uri uri,
        TcpClient? tcpClient,
        X509CertificateCollection? x509CertificateCollection,
        SslProtocols tlsProtocolType)
    {
        if (tcpClient is null)
        {
            throw new NullReferenceException("Tcp Client cannot be null when trying to get socket stream.");
        }

        connectionStatusAction(ConnectionStatus.ConnectingToSocketStream, null);

        if (isSecureConnectionSchemeFunc())
        {
            var secureStream = new SslStream(
                innerStream: tcpClient.GetStream(),
                leaveInnerStreamOpen: true,
                userCertificateValidationCallback: (sender, cert, chain, tlsPolicy) 
                    => validateServerCertificateFunc(sender, cert, chain, tlsPolicy));

            try
            {
                await secureStream.AuthenticateAsClientAsync(uri.Host, x509CertificateCollection, tlsProtocolType, false);

                connectionStatusAction(ConnectionStatus.SecureSocketStreamConnected, null);
                return secureStream;
            }
            catch (Exception ex)
            {
                throw new WebsocketClientLiteException("Unable to determine stream type", ex);
            }
        }

        connectionStatusAction(ConnectionStatus.SocketStreamConnected, null);
        return tcpClient.GetStream();
    }

    public void Dispose()
    {
        _stream?.Dispose();

        if (!_keepTcpClientAlive)
        {
            tcpClient?.Dispose();
        }
    }
}
