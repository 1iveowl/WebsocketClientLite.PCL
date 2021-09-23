using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public interface IMessageWebSocketRx : IDisposable
    {
        //bool IsConnected { get; }
        bool SubprotocolAccepted { get; }
        string SubprotocolAcceptedName { get; }
        string Origin { get; }

        IDictionary<string, string> Headers { get; }

        IEnumerable<string> Subprotocols { get; }

        SslProtocols TlsProtocolType { get; }
        
        bool ExcludeZeroApplicationDataInPong { get; }

        bool IgnoreServerCertificateErrors { get; }

        IObservable<ConnectionStatus> ConnectionStatusObservable { get; }
        IObservable<string> MessageReceiverObservable { get; }

        X509CertificateCollection X509CertCollection { get; }

        Task ConnectAsync(
            Uri uri,
            TimeSpan timeout = default);

        Task DisconnectAsync();

        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);
        Task SendTextMultiFrameAsync(string message, FrameType frameType);

        bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors);
    }
}
