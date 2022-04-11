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

        private TcpClient _tcpClient;
        private Stream _stream;
        
        private readonly Func<bool> _isSecureConnectionSchemeFunc;
        private readonly Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> _validateServerCertificateFunc;
        private readonly Func<TcpClient, Uri, Task> _connectTcpClient;
        private readonly Func<Stream, byte[], CancellationToken, Task<int>> _readOneByteFunc;

        internal Stream ConnectionStream => _stream;

        public TcpConnectionService(           
            Func<bool> isSecureConnectionSchemeFunc,
            Func<object, X509Certificate, X509Chain, SslPolicyErrors, bool> validateServerCertificateFunc,
            Func<TcpClient, Uri, Task> connectTcpClientFunc,
            Func<Stream, byte[], CancellationToken, Task<int>> readOneByteFunc,
            TcpClient tcpClient = null)
        {
            _keepTcpClientAlive = tcpClient is not null;
            
            _isSecureConnectionSchemeFunc = isSecureConnectionSchemeFunc;
            _validateServerCertificateFunc = validateServerCertificateFunc;
            _connectTcpClient = connectTcpClientFunc;
            _readOneByteFunc = readOneByteFunc;

            _tcpClient = tcpClient;
        }

        internal virtual async Task<Stream> ConnectTcpClientAndStream(
            Uri uri,
            Action reportConnected,
            X509CertificateCollection x509CertificateCollection,
            SslProtocols tlsProtocolType,
            TimeSpan timeout = default)
        {
            await ConnectTcpClient(uri, reportConnected, timeout);

            _stream = await CreateStream(uri, _tcpClient, x509CertificateCollection, tlsProtocolType);

            return _stream;
        }

        internal IObservable<byte[]> ByteStreamObservable() =>
            Observable.Defer(() => Observable.FromAsync(ct => ReadOneByteFromStream(ct)))
            .Repeat()
            .TakeWhile(@byte => @byte is not null)
            .Publish()
            .RefCount();

        internal async Task<byte[]> ReadOneByteFromStream(CancellationToken ct)
        {
            if(_stream is null || !_stream.CanRead)
            {
                throw new WebsocketClientLiteException("Stream not ready or not connected.");
            }

            var @byte = new byte[1];

            try
            {
                if (_stream == null)
                {
                    throw new WebsocketClientLiteException("Read stream cannot be null.");
                }

                if (!_stream.CanRead)
                {
                    throw new WebsocketClientLiteException("Websocket connection have been closed.");
                }

                var length = await _readOneByteFunc(_stream, @byte, ct);

                if (length == 0)
                {
                    throw new WebsocketClientLiteException("Websocket connection aborted unexpectedly. Check connection and socket security version/TLS version).");
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("Ignoring Object Disposed Exception - This is an expected exception");
            }
            return @byte;
        }

        private async Task ConnectTcpClient(
            Uri uri,
            Action reportConnected,
            TimeSpan timeout = default)
        {
            if (_tcpClient is null)
            {
                _tcpClient = new TcpClient(
                    uri.HostNameType == UriHostNameType.IPv6
                        ? AddressFamily.InterNetworkV6
                        : AddressFamily.InterNetwork);
            }

            try
            {
                await _connectTcpClient(_tcpClient, uri)
                    .ToObservable()
                    .Timeout(timeout != default ? timeout : TimeSpan.FromSeconds(5));
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
                reportConnected();
                Debug.WriteLine("Connected");
            }
            else
            {
                throw new WebsocketClientLiteTcpConnectException($"Unable to connect to Tcp socket for: {uri.Host}:{uri.Port}.");
            }
        }

        private async Task<Stream> CreateStream(
            Uri uri,
            TcpClient tcpClient,
            X509CertificateCollection x509CertificateCollection,
            SslProtocols tlsProtocolType)
        {
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

                    return secureStream;
                }
                catch (Exception ex)
                {
                    throw new WebsocketClientLiteException("Unable to determine stream type", ex);
                }
            }

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
