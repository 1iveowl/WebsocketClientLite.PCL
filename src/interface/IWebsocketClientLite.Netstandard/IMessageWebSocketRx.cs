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
    public interface IMessageWebSocketRx : IDisposable
    {
        IObservable<string> ObserveTextMessagesReceived { get; }

        IObservable<ConnectionStatus> ObserveConnectionStatus { get; }

        bool IsConnected { get; }

        bool SubprotocolAccepted { get; }

        string SubprotocolAcceptedName { get; }

        Task ConnectAsync(
            Uri uri, 
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subProtocols = null, 
            bool ignoreServerCertificateErrors = false, 
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12,
            bool excludeZeroApplicationDataInPong = false);

        Task CloseAsync();

        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);

        Task SendTextMultiFrameAsync(string message, FrameType frameType);
    }
}
