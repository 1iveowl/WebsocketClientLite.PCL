using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using ISocketLite.PCL.Interface;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.Helper;
using WebsocketClientLite.PCL.Model;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebSocketConnectService
    {

        #region Obsolete

        //private CancellationTokenSource _innerCancellationTokenSource;

        //[Obsolete("Deprecated")]
        //internal async Task ConnectAsync(
        //    Uri uri,
        //    bool secure,
        //    HttpParserDelegate requestHandler,
        //    HttpCombinedParser parserHandler,
        //    CancellationTokenSource innerCancellationTokenSource,
        //    WebsocketListener websocketListener,
        //    string origin = null,
        //    IDictionary<string, string> headers = null,
        //    IEnumerable<string> subprotocols = null,
        //    bool ignoreServerCertificateErrors = false,
        //    TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12)
        //{
        //    TcpSocketClient = new TcpSocketClient();

        //    try
        //    {
        //        _innerCancellationTokenSource = innerCancellationTokenSource;

        //        await TcpSocketClient.ConnectAsync(
        //            uri.Host,
        //            uri.Port.ToString(),
        //            secure,
        //            innerCancellationTokenSource.Token,
        //            ignoreServerCertificateErrors,
        //            tlsProtocolType);

        //        if (TcpSocketClient.IsConnected)
        //        {
        //            websocketListener.DataReceiveMode = DataReceiveMode.IsListeningForHandShake;

        //            websocketListener.Start(requestHandler, parserHandler, innerCancellationTokenSource);

        //            await SendConnectHandShakeAsync(uri, secure, origin, headers, subprotocols);

        //            var waitForHandShakeLoopTask = Task.Run(async () =>
        //            {
        //                while (!requestHandler.HttpRequestReponse.IsEndOfMessage
        //                       && !requestHandler.HttpRequestReponse.IsRequestTimedOut
        //                       && !requestHandler.HttpRequestReponse.IsUnableToParseHttp)
        //                {
        //                    await Task.Delay(TimeSpan.FromMilliseconds(10));
        //                }
        //            });

        //            var timeout = Task.Delay(TimeSpan.FromSeconds(10));

        //            var taskReturn = await Task.WhenAny(waitForHandShakeLoopTask, timeout);

        //            if (taskReturn == timeout)
        //            {
        //                throw new TimeoutException("Connection request to server timed out");
        //            }

        //            parserHandler.Execute(default(ArraySegment<byte>));

        //            if (requestHandler.HttpRequestReponse.IsUnableToParseHttp)
        //            {
        //                throw new Exception("Invalid response from websocket server");
        //            }

        //            if (requestHandler.HttpRequestReponse.IsRequestTimedOut)
        //            {
        //                throw new TimeoutException("Connection request to server timed out");
        //            }

        //            if (requestHandler.HttpRequestReponse.StatusCode != 101)
        //            {
        //                throw new Exception($"Unable to connect to websocket Server. " +
        //                                    $"Error code: {requestHandler.HttpRequestReponse.StatusCode}, " +
        //                                    $"Error reason: {requestHandler.HttpRequestReponse.ResponseReason}");
        //            }

        //            Debug.WriteLine("HandShake completed");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw ex;
        //    }

        //    parserHandler.Execute(default(ArraySegment<byte>));
        //}

        //[Obsolete("Deprecated")]
        //internal void Disconnect()
        //{
        //    _innerCancellationTokenSource?.Cancel();
        //    TcpSocketClient.Disconnect();
        //}

        #endregion

        internal ITcpSocketClient TcpSocketClient;

        private readonly ISubject<ConnectionStatus> _subjectConnectionStatus;

        internal WebSocketConnectService(ISubject<ConnectionStatus> subjectConnectionStatus)
        {
            _subjectConnectionStatus = subjectConnectionStatus;
        }

        internal async Task ConnectServer(
            Uri uri,
            bool secure,
            ITcpSocketClient tcpSocketClient,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subprotocols = null)
        {
            TcpSocketClient = tcpSocketClient;
            _subjectConnectionStatus.OnNext(ConnectionStatus.Connecting);

            await SendConnectHandShakeAsync(uri, secure, origin, headers, subprotocols);

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
                _subjectConnectionStatus.OnNext(ConnectionStatus.Aborted);
                throw new WebsocketClientLiteException("Unable to complete handshake", ex.InnerException);
            }
        }
    }
}
