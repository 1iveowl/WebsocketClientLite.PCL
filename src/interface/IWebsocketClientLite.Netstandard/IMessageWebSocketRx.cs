using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

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

        X509CertificateCollection X509CertCollection { get; }

        bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors);

        bool IsSecureConnectionScheme(Uri uri);

        ISender GetSender();

        IObservable<IDataframe> WebsocketConnectObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingTimeSpan = default,
            TimeSpan timeout = default);

        IObservable<(IDataframe dataframe, ConnectionStatus state)> WebsocketConnectWithStatusObservable(
            Uri uri, 
            bool hasClientPing = false,
            TimeSpan clientPingTimeSpan = default, 
            TimeSpan timeout = default);

    }
}
