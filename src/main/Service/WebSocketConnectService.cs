using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HttpMachine;
using ISocketLite.PCL.Interface;
using ISocketLite.PCL.Model;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Parser;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectService
    {
        private readonly ITcpSocketClient _client;
        private CancellationTokenSource _cancellationTokenSource;

        internal WebSocketConnectService(ITcpSocketClient client)
        {
            _client = client;
        }

        internal async Task ConnectAsync(
            Uri uri,
            bool secure,
            HttpParserDelegate requestHandler,
            HttpCombinedParser parserHandler,
            CancellationTokenSource cancellationTokenSource,
            WebsocketListener websocketListener,
            IEnumerable<string> subprotocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12)
        {
            try
            {
                _cancellationTokenSource = cancellationTokenSource;

                await _client.ConnectAsync(
                    uri.Host,
                    uri.Port.ToString(),
                    secure,
                    cancellationTokenSource.Token,
                    ignoreServerCertificateErrors,
                    tlsProtocolType);

                websocketListener.DataReceiveMode = DataReceiveMode.IsListeningForHandShake;

                websocketListener.Start(requestHandler, parserHandler);

                await SendConnectHandShakeAsync(uri, secure, subprotocols);

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
            _cancellationTokenSource.Cancel();
            _client.Disconnect();

        }

        private async Task SendConnectHandShakeAsync(Uri uri, bool secure, IEnumerable<string> subprotocols = null)
        {
            var handShake = ClientHandShake.Compose(uri, secure, subprotocols);
            try
            {
                await _client.WriteStream.WriteAsync(handShake, 0, handShake.Length);
                await _client.WriteStream.FlushAsync();
            }
            catch (Exception ex)
            {
                
                throw ex;
            }
            
        }
    }
}
