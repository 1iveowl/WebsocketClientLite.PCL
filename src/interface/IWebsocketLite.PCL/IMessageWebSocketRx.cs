using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ISocketLite.PCL.Model;

namespace IWebsocketClientLite.PCL
{
    public interface IMessageWebSocketRx
    {
        IObservable<string> ObserveTextMessagesReceived { get; }

        bool IsConnected { get; }

        bool SubprotocolAccepted { get; }

        string SubprotocolAcceptedName { get; }

        void SetRequestHeader(string headerName, string headerValue);

        Task ConnectAsync(
            Uri uri, 
            CancellationTokenSource outerCancellationTokenSource, 
            IEnumerable<string> subProtocols = null, 
            bool ignoreServerCertificateErrors = false, 
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12);

        Task CloseAsync();

        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);

        Task SendTextMultiFrameAsync(string message, FrameType frameType);
    }
}
