using IWebsocketClientLite.PCL;
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
using WebsocketClientLite.PCL.CustomException;

namespace WebsocketClientLite.PCL.Service
{
    internal class TcpConnectionService : IDisposable
    {
        private readonly bool _keepTcpClientAlive;      
        private readonly Func<bool> _isSecureConnectionSchemeFunc;
        private readonly Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> _validateServerCertificateFunc;
        private readonly Func<TcpClient, Uri, Task> _connectTcpClient;
        private readonly Func<Stream, byte[], CancellationToken, Task<int>> _readOneByteFunc;
        private readonly Action<ConnectionStatus, Exception> _connectionStatusAction;

        private TcpClient _tcpClient;
        private Stream _stream;

        internal Stream ConnectionStream => _stream;

        public TcpConnectionService(           
            Func<bool> isSecureConnectionSchemeFunc,
            Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
            Func<TcpClient, Uri, Task> connectTcpClientFunc,
            Func<Stream, byte[], CancellationToken, Task<int>> readOneByteFunc,
            Action<ConnectionStatus, Exception> connectionStatusAction,
            TcpClient tcpClient = null)
        {
            _keepTcpClientAlive = tcpClient is not null;
            
            _isSecureConnectionSchemeFunc = isSecureConnectionSchemeFunc;
            _validateServerCertificateFunc = validateServerCertificateFunc;
            _connectTcpClient = connectTcpClientFunc;
            _connectionStatusAction = connectionStatusAction;
            _readOneByteFunc = readOneByteFunc;

            _tcpClient = tcpClient;
        }

        internal virtual async Task ConnectTcpStream(
            Uri uri,
            X509CertificateCollection x509CertificateCollection,
            SslProtocols tlsProtocolType,
            TimeSpan timeout = default)
        {
            await ConnectTcpClient(uri, timeout);

            _stream = await GetTcpStream(uri, _tcpClient, x509CertificateCollection, tlsProtocolType);
        }

        internal IObservable<byte[]> BytesObservable() =>
            Observable.Defer(() => Observable.FromAsync(ct => ReadByteArrayFromStream(1, ct)))
            .Where(bytes => bytes is not null);

        internal async Task<byte[]> ReadBytesFromStream(ulong size, CancellationToken ct) =>
            await ReadByteArrayFromStream(size, ct);

        internal async Task<byte[]> ReadByteArrayFromStream(ulong size, CancellationToken ct)
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

                var readLength = await _readOneByteFunc(_stream, byteArray, ct);

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
            _connectionStatusAction(ConnectionStatus.ConnectingToTcpSocket, null);

            if (_tcpClient is null)
            {
                _tcpClient = new TcpClient(
                    uri.HostNameType is UriHostNameType.IPv6
                        ? AddressFamily.InterNetworkV6
                        : AddressFamily.InterNetwork);
            }

            try
            {
                if (!_tcpClient.Connected)
                {
                    await _connectTcpClient(_tcpClient, uri)
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

            if (_tcpClient.Connected)
            {
                _connectionStatusAction(ConnectionStatus.TcpSocketConnected, null);
                Debug.WriteLine("Connected");
            }
            else
            {
                throw new WebsocketClientLiteTcpConnectException($"Unable to connect to Tcp socket for: {uri.Host}:{uri.Port}.");
            }
        }

        private async Task<Stream> GetTcpStream(
            Uri uri,
            TcpClient tcpClient,
            X509CertificateCollection x509CertificateCollection,
            SslProtocols tlsProtocolType)
        {
            _connectionStatusAction(ConnectionStatus.ConnectingToSocketStream, null);

            if (_isSecureConnectionSchemeFunc())
            {
                var secureStream = new SslStream(
                    innerStream: tcpClient.GetStream(),
                    leaveInnerStreamOpen: true,
                    userCertificateValidationCallback: (sender, cert, chain, tlsPolicy) 
                        => _validateServerCertificateFunc(sender, cert, chain, tlsPolicy));

                try
                {
                    await secureStream.AuthenticateAsClientAsync(uri.Host, x509CertificateCollection, tlsProtocolType, false);

                    _connectionStatusAction(ConnectionStatus.SecureSocketStreamConnected, null);
                    return secureStream;
                }
                catch (Exception ex)
                {
                    throw new WebsocketClientLiteException("Unable to determine stream type", ex);
                }
            }

            _connectionStatusAction(ConnectionStatus.SocketStreamConnected, null);
            return tcpClient.GetStream();
        }

        public void Dispose()
        {
            _stream?.Dispose();

            if (!_keepTcpClientAlive)
            {
                _tcpClient?.Dispose();
            }
        }
    }
}
