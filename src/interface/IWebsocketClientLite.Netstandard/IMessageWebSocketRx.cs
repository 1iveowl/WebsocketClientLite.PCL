using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public interface IMessageWebSocketRx : IDisposable
    {
        bool IsConnected { get; }

        bool SubprotocolAccepted { get; }

        //IEnumerable<string> SubprotocolAcceptedNames { get; }

        string Origin { get; }

        IDictionary<string, string> Headers { get; }

        IEnumerable<string> Subprotocols { get; }

        SslProtocols TlsProtocolType { get; }
        
        bool ExcludeZeroApplicationDataInPong { get; }

        bool IgnoreServerCertificateErrors { get; }

        //[Obsolete("Use ConnectObservable instead")]
        IObservable<ConnectionStatus> ConnectionStatusObservable { get; }

        //[Obsolete("Use ConnectObservable instead.")]
        //IObservable<string> MessageReceiverObservable { get; }

        X509CertificateCollection X509CertCollection { get; }

        bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors);

        bool IsSecureConnectionScheme(Uri uri);

        //[Obsolete("Use ConnectObservable instead.")]
        //Task ConnectAsync(
        //    Uri uri,
        //    TimeSpan timeout = default);

        IObservable<string> WebsocketConnectObservable(Uri uri, TimeSpan timeout = default);

        ISender GetSender();

        //[Obsolete("Use ConnectObservable instead. To disconnect simply dispose of the message observable.")]
        //Task DisconnectAsync();

        
    }
}
