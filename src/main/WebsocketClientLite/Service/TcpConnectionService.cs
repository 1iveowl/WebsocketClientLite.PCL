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
    Action<ConnectionStatus, Exception?> connectionStatusAction,
    bool hasTransferTcpSocketLifeCycleOwnership,
    TcpClient? tcpClient = null,
    int maxFrameSize = 0,
    bool checkCertificateRevocation = false) : IDisposable
{
    private readonly bool _keepTcpClientAlive = !hasTransferTcpSocketLifeCycleOwnership;
    private bool _ownsCreatedTcpClient;
    private Stream? _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    // Effective per-frame / per-message payload cap. A value <= 0 means "no
    // explicit limit" (bounded only by the int.MaxValue array-size limit).
    internal int MaxFrameSize { get; } = maxFrameSize <= 0 ? int.MaxValue : maxFrameSize;

    internal Stream ConnectionStream => _stream ?? throw new ArgumentNullException("Stream cannot be null");

    // Serializes writes to the connection stream. Neither SslStream nor
    // NetworkStream supports concurrent writes, and the client may write from
    // several sources at once (user sends, periodic client pings, and automatic
    // pong replies). Without serialization their bytes interleave on the wire,
    // producing malformed frames that cause the server to drop the connection.
    internal async Task WriteSerializedAsync(Func<Stream, CancellationToken, Task> writeAsync, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await writeAsync(ConnectionStream, ct).ConfigureAwait(false);
        }
        finally
        {
            // The gate may already be disposed if the connection was torn down
            // while this write was in flight.
            try { _writeGate.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    internal ValueTask ConnectTcpStream(
        Uri uri,
        X509CertificateCollection? x509CertificateCollection,
        SslProtocols tlsProtocolType,
        TimeSpan timeout = default) => new(ConnectTcpStreamCore(uri, x509CertificateCollection, tlsProtocolType, timeout));

    private async Task ConnectTcpStreamCore(
        Uri uri,
        X509CertificateCollection? x509CertificateCollection,
        SslProtocols tlsProtocolType,
        TimeSpan timeout)
    {
        await ConnectTcpClient(uri, timeout).ConfigureAwait(false);
        _stream = await GetTcpStream(uri, tcpClient, x509CertificateCollection, tlsProtocolType).ConfigureAwait(false);
    }

    internal ValueTask<byte[]?> ReadBytesFromStream(ulong size, CancellationToken ct) =>
        ReadByteArrayFromStream(size, ct);

    internal async ValueTask<byte[]?> ReadByteArrayFromStream(ulong size, CancellationToken ct)
    {
        if (_stream is null || !_stream.CanRead)
        {
            throw new WebsocketClientLiteException("Stream not ready or not connected.");
        }

        // We cannot allocate arrays larger than int.MaxValue
        int requested = checked((int)Math.Min((ulong)int.MaxValue, size));
        var buffer = new byte[requested];
        int totalRead = 0;

        try
        {
            while (totalRead < requested)
            {
#if NETSTANDARD2_0
                int read = await _stream.ReadAsync(buffer, totalRead, requested - totalRead, ct).ConfigureAwait(false);
#else
                int read = await _stream.ReadAsync(buffer.AsMemory(totalRead, requested - totalRead), ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    // Unexpected EOF
                    throw new WebsocketClientLiteException("Websocket connection aborted unexpectedly. Check connection and socket security version/TLS version).");
                }

                totalRead += read;
            }
        }
        catch (OperationCanceledException)
        {
            // Align with prior behavior (readOneByteFunc returned -1 on cancel)
            return null;
        }
        catch (ObjectDisposedException)
        {
            // Stream was disposed (e.g. connection torn down). Treat like
            // cancellation: signal "no data" rather than returning a partially
            // filled/zeroed buffer that would be misread as a valid frame.
            Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
            return null;
        }

        return buffer;
    }

    private async Task ConnectTcpClient(
        Uri uri,
        TimeSpan timeout = default)
    {
        connectionStatusAction(ConnectionStatus.ConnectingToTcpSocket, null);

        if (tcpClient is null)
        {
            // Reassign the captured field (not a shadowing local) so the created
            // client is actually used. We allocated it, so we own its lifetime
            // and dispose it in Dispose() regardless of the ownership flag.
            tcpClient = new TcpClient(
                uri.HostNameType is UriHostNameType.IPv6
                    ? AddressFamily.InterNetworkV6
                    : AddressFamily.InterNetwork);
            _ownsCreatedTcpClient = true;
        }

        try
        {
            if (!tcpClient!.Connected)
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

        if (tcpClient!.Connected)
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
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(tcpClient, "Tcp Client cannot be null when trying to get socket stream."); 
#else
        if (tcpClient is null)
        {
            throw new ArgumentNullException("Tcp Client cannot be null when trying to get socket stream.");
        }
#endif

        connectionStatusAction(ConnectionStatus.ConnectingToSocketStream, null);

        if (isSecureConnectionSchemeFunc())
        {
            var secureStream = new SslStream(
                innerStream: tcpClient.GetStream(),
                leaveInnerStreamOpen: true,
                userCertificateValidationCallback: (sender, cert, chain, tlsPolicy) 
                    => validateServerCertificateFunc(
                        sender, 
                        cert ?? throw new InvalidOperationException("Server certificate is null."), 
                        chain ?? new X509Chain(), 
                        tlsPolicy));

            try
            {
                await secureStream.AuthenticateAsClientAsync(uri.Host, x509CertificateCollection, tlsProtocolType, checkCertificateRevocation).ConfigureAwait(false);
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
        _writeGate.Dispose();

        if (!_keepTcpClientAlive || _ownsCreatedTcpClient)
        {
            tcpClient?.Dispose();
        }
    }
}
