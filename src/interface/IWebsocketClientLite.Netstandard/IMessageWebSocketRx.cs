using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ISocketLite.PCL.Model;

namespace IWebsocketClientLite.PCL
{
    public interface IMessageWebSocketRx : IDisposable
    {
        #region Obsolete

        //[Obsolete("Deprecated")]
        //IObservable<string> ObserveTextMessagesReceived { get; }

        //[Obsolete("Deprecated")]
        //Task ConnectAsync(
        //    Uri uri,
        //    string origin = null,
        //    IDictionary<string, string> headers = null,
        //    IEnumerable<string> subProtocols = null,
        //    bool ignoreServerCertificateErrors = false,
        //    TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12,
        //    bool excludeZeroApplicationDataInPong = false);
        
        #endregion

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
            TlsProtocolVersion tlsProtocolType = TlsProtocolVersion.Tls12,
            bool excludeZeroApplicationDataInPong = false,
            CancellationToken token = default (CancellationToken));

        Task CloseAsync();

        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);
        Task SendTextMultiFrameAsync(string message, FrameType frameType);
    }
}
