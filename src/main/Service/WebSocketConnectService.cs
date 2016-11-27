using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HttpMachine;
using ISocketLite.PCL.Interface;
using ISocketLite.PCL.Model;
using SocketLite.Services;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectService
    {
        internal ITcpSocketClient TcpSocketClient;
        private CancellationTokenSource _innerCancellationTokenSource;

        internal WebSocketConnectService()
        {
        }

        internal async Task ConnectAsync(
            Uri uri,
            bool secure,
            HttpParserDelegate requestHandler,
            HttpCombinedParser parserHandler,
            CancellationTokenSource innerCancellationTokenSource,
            WebsocketListener websocketListener,
            string origin = null,
            IEnumerable<string> subprotocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12)
        {
            TcpSocketClient = new TcpSocketClient();

            try
            {
                _innerCancellationTokenSource = innerCancellationTokenSource;

                await TcpSocketClient.ConnectAsync(
                    uri.Host,
                    uri.Port.ToString(),
                    secure,
                    innerCancellationTokenSource.Token,
                    ignoreServerCertificateErrors,
                    tlsProtocolType);

                websocketListener.DataReceiveMode = DataReceiveMode.IsListeningForHandShake;

                websocketListener.Start(requestHandler, parserHandler, innerCancellationTokenSource);

                await SendConnectHandShakeAsync(uri, secure, origin, subprotocols);

                var waitForHandShakeLoopTask = Task.Run(async () =>
                {
                    while (!requestHandler.HttpRequestReponse.IsEndOfMessage
                           && !requestHandler.HttpRequestReponse.IsRequestTimedOut
                           && !requestHandler.HttpRequestReponse.IsUnableToParseHttp)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }
                });

                var timeout = Task.Delay(TimeSpan.FromSeconds(10));

                var taskReturn = await Task.WhenAny(waitForHandShakeLoopTask, timeout);

                if (taskReturn == timeout)
                {
                    throw new TimeoutException("Connection request to server timed out");
                }

                parserHandler.Execute(default(ArraySegment<byte>));

                if (requestHandler.HttpRequestReponse.IsUnableToParseHttp)
                {
                    throw new Exception("Invalid response from websocket server");
                }

                if (requestHandler.HttpRequestReponse.IsRequestTimedOut)
                {
                    throw new TimeoutException("Connection request to server timed out");
                }

                if (requestHandler.HttpRequestReponse.StatusCode != 101)
                {
                    throw new Exception($"Unable to connect to websocket Server. " +
                                        $"Error code: {requestHandler.HttpRequestReponse.StatusCode}, " +
                                        $"Error reason: {requestHandler.HttpRequestReponse.ResponseReason}");
                }

                System.Diagnostics.Debug.WriteLine("HandShake completed");
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
            }
            parserHandler.Execute(default(ArraySegment<byte>));
        }

        internal void Disconnect()
        {
            TcpSocketClient.Disconnect();
            _innerCancellationTokenSource.Cancel();

        }

        private async Task SendConnectHandShakeAsync(Uri uri, bool secure, string origin = null, IEnumerable<string> subprotocols = null)
        {
            var handShake = ClientHandShake.Compose(uri, secure, origin, subprotocols);
            try
            {
                await TcpSocketClient.WriteStream.WriteAsync(handShake, 0, handShake.Length);
                await TcpSocketClient.WriteStream.FlushAsync();
            }
            catch (Exception ex)
            {
                
                throw ex;
            }
            
        }
    }
}
