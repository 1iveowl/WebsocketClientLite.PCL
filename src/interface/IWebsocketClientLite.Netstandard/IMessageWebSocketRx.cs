using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ISocketLite.PCL.Model;

namespace IWebsocketClientLite.PCL
{
    public interface IMessageWebSocketRx : IDisposable
    {
        #region Obsolete

        [Obsolete("Deprecated")]
        IObservable<string> ObserveTextMessagesReceived { get; }

        [Obsolete("Deprecated")]
        Task ConnectAsync(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subProtocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12,
            bool excludeZeroApplicationDataInPong = false);

        [Obsolete("Deprecated")]
        Task CloseAsync();

        #endregion

        Task<IObservable<string>> CreateObservableMessageReceiver(
            Uri uri,
            string origin = null,
            IDictionary<string, string> headers = null,
            IEnumerable<string> subProtocols = null,
            bool ignoreServerCertificateErrors = false,
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12,
            bool excludeZeroApplicationDataInPong = false);

        IObservable<ConnectionStatus> ObserveConnectionStatus { get; }

        bool IsConnected { get; }

        bool SubprotocolAccepted { get; }

        string SubprotocolAcceptedName { get; }

        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);
        Task SendTextMultiFrameAsync(string message, FrameType frameType);
    }
}
