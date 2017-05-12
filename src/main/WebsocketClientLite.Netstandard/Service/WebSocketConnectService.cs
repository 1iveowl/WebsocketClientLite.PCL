using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
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

        #region Obsolete

        [Obsolete("Deprecated")]
        internal async Task ConnectAsync(
            Uri uri,
            bool secure,
            HttpParserDelegate requestHandler,
            HttpCombinedParser parserHandler,
            CancellationTokenSource innerCancellationTokenSource,
            WebsocketListener websocketListener,
            string origin = null,
            IDictionary<string, string> headers = null,
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

                if (TcpSocketClient.IsConnected)
                {
                    websocketListener.DataReceiveMode = DataReceiveMode.IsListeningForHandShake;

                    websocketListener.Start(requestHandler, parserHandler, innerCancellationTokenSource);

                    await SendConnectHandShakeAsync(uri, secure, origin, headers, subprotocols);

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

                    Debug.WriteLine("HandShake completed");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            parserHandler.Execute(default(ArraySegment<byte>));
        }

        [Obsolete("Deprecated")]
        internal void Disconnect()
        {
            _innerCancellationTokenSource.Cancel();
            TcpSocketClient.Disconnect();
        }

        #endregion
        //private ITcpSocketClient _tcpSocketClient;

        private CancellationTokenSource _innerCancellationTokenSource;

        internal ITcpSocketClient TcpSocketClient;

        internal bool IsConnected;

        internal WebSocketConnectService()
        {
        }

        private readonly HandshakeParser _handshakeParser = new HandshakeParser();

        internal async Task ConnectServer(
            Uri uri,
            bool secure,
            HttpParserDelegate requestHandler,
            HttpCombinedParser parserHandler,
            CancellationTokenSource innerCancellationTokenSource,
            WebsocketListener websocketListener,
            ITcpSocketClient tcpSocketClient,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12)
        {
            TcpSocketClient = tcpSocketClient;

            IsConnected = false;

            var observableConnect = Observable.Create<bool>(
                obs =>
                {
                    var disp = TcpSocketClient.ReadStream.ReadOneByteAtTheTimeObservable(innerCancellationTokenSource)
                    .Subscribe(
                        b =>
                        {
                            try
                            {
                                _handshakeParser.Parse(b, requestHandler, parserHandler);

                                if (requestHandler.HttpRequestReponse.IsEndOfMessage)
                                {
                                    obs.OnNext(true);
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (ex is TimeoutException)
                                {
                                    requestHandler.HttpRequestReponse.IsRequestTimedOut = true;
                                }
                                else
                                {
                                    requestHandler.HttpRequestReponse.IsUnableToParseHttp = true;
                                }
                                obs.OnNext(false);
                            }
                        },
                        ex => obs.OnError(ex),
                        () =>
                        {
                            parserHandler.Execute(default(ArraySegment<byte>));
                            obs.OnCompleted();
                        });

                    return disp;

                });

            var observableWaitForHandshake = Observable.While(
                () => !requestHandler.HttpRequestReponse.IsEndOfMessage 
                && !requestHandler.HttpRequestReponse.IsRequestTimedOut 
                && !requestHandler.HttpRequestReponse.IsUnableToParseHttp,
                observableConnect);

            var subscribeHandshake = observableWaitForHandshake.Subscribe(
                    isHandshakecomplete =>
                    {
                        if (isHandshakecomplete)
                        {
                            if (requestHandler.HttpRequestReponse.StatusCode == 101)
                            {
                                IsConnected = true;

                                System.Diagnostics.Debug.WriteLine("HandShake completed");
                            }
                            else
                            {
                                throw new Exception($"Unable to connect to websocket Server. " +
                                                    $"Error code: {requestHandler.HttpRequestReponse.StatusCode}, " +
                                                    $"Error reason: {requestHandler.HttpRequestReponse.ResponseReason}");
                            }
                        }
                        else
                        {
                            if (requestHandler.HttpRequestReponse.IsRequestTimedOut)
                            {
                                throw new TimeoutException("Connection request to server timed out");
                            }

                            throw new Exception("Invalid response from websocket server");
                        }
                    });

            await SendConnectHandShakeAsync(uri, secure, origin, headers, subprotocols);

            var waitForHandShakeToComplete = Task.Run(async () =>
            {
                while (!requestHandler.HttpRequestReponse.IsEndOfMessage
                       && !requestHandler.HttpRequestReponse.IsRequestTimedOut
                       && !requestHandler.HttpRequestReponse.IsUnableToParseHttp)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                }
            });
            
            var timeout = Task.Delay(TimeSpan.FromSeconds(10));

            await Task.WhenAny(waitForHandShakeToComplete, timeout);
            
            subscribeHandshake.Dispose();
    
            //IsConnected = false;

            //try
            //{
            //    _innerCancellationTokenSource = innerCancellationTokenSource;

            //    if (TcpSocketClient.IsConnected)
            //    {
            //        websocketListener.DataReceiveMode = DataReceiveMode.IsListeningForHandShake;

            //        //observableListener = websocketListener.CreateObservableListener(requestHandler, parserHandler, innerCancellationTokenSource);

            //        await SendConnectHandShakeAsync(uri, secure, origin, headers, subprotocols);

            //        var waitForHandShakeLoopTask = Task.Run(async () =>
            //        {
            //            while (!requestHandler.HttpRequestReponse.IsEndOfMessage
            //                   && !requestHandler.HttpRequestReponse.IsRequestTimedOut
            //                   && !requestHandler.HttpRequestReponse.IsUnableToParseHttp)
            //            {
            //                await Task.Delay(TimeSpan.FromMilliseconds(10));
            //            }
            //        });

            //        var timeout = Task.Delay(TimeSpan.FromSeconds(10));

            //        var taskReturn = await Task.WhenAny(waitForHandShakeLoopTask, timeout);

            //        if (taskReturn == timeout)
            //        {
            //            throw new TimeoutException("Connection request to server timed out");
            //        }

            //        parserHandler.Execute(default(ArraySegment<byte>));

            //        if (requestHandler.HttpRequestReponse.IsUnableToParseHttp)
            //        {
            //            throw new Exception("Invalid response from websocket server");
            //        }

            //        if (requestHandler.HttpRequestReponse.IsRequestTimedOut)
            //        {
            //            throw new TimeoutException("Connection request to server timed out");
            //        }

            //        if (requestHandler.HttpRequestReponse.StatusCode != 101)
            //        {
            //            throw new Exception($"Unable to connect to websocket Server. " +
            //                                $"Error code: {requestHandler.HttpRequestReponse.StatusCode}, " +
            //                                $"Error reason: {requestHandler.HttpRequestReponse.ResponseReason}");
            //        }

            //        IsConnected = true;
            //        System.Diagnostics.Debug.WriteLine("HandShake completed");
            //    }
            //}
            //catch (Exception ex)
            //{
            //    throw ex;
            //}

            //parserHandler.Execute(default(ArraySegment<byte>));

            //return observableListener;
        }

        private async Task SendConnectHandShakeAsync(
            Uri uri, 
            bool secure, 
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            var handShake = ClientHandShake.Compose(uri, secure, origin, headers, subprotocols);
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
