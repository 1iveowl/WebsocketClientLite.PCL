using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public interface IMessageWebSocketRx : IDisposable
    {

        bool IsConnected { get; }

        bool SubprotocolAccepted { get; }

        string SubprotocolAcceptedName { get; }

        IObservable<ConnectionStatus> ObserveConnectionStatus { get; }

        Task<IObservable<string>> CreateObservableMessageReceiver(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subProtocols = null,
            bool ignoreServerCertificateErrors = false,
            SslProtocols tlsProtocolType = SslProtocols.Tls12,
            bool excludeZeroApplicationDataInPong = false,
            CancellationToken token = default (CancellationToken));

        Task CloseAsync();

        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);
        Task SendTextMultiFrameAsync(string message, FrameType frameType);
    }
}
