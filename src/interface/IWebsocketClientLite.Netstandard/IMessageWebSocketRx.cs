using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IWebsocketClientLite.PCL
{
    /// <summary>
    /// 1iveowl Light Weight Websocket Client
    /// </summary>
    public interface IMessageWebSocketRx : IDisposable
    {
        /// <summary>
        /// Is Websocket client connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Origin
        /// </summary>
        string Origin { get; }

        /// <summary>
        /// Headers
        /// </summary>
        IDictionary<string, string> Headers { get; }

        /// <summary>
        /// Subprotocols
        /// </summary>
        IEnumerable<string> Subprotocols { get; }

        /// <summary>
        /// Tls protocol used
        /// </summary>
        SslProtocols TlsProtocolType { get; }
        
        /// <summary>
        /// Typically used with Slack. See documentation. 
        /// </summary>
        bool ExcludeZeroApplicationDataInPong { get; }

        /// <summary>
        /// Use with care. See documentation.
        /// </summary>
        bool IgnoreServerCertificateErrors { get; }

        /// <summary>
        /// X.509 certificate collection.
        /// </summary>
        X509CertificateCollection X509CertCollection { get; }

        /// <summary>
        /// Validating Server Certificate.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="certificate"></param>
        /// <param name="chain"></param>
        /// <param name="sslPolicyErrors"></param>
        /// <returns></returns>
        bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors);

        /// <summary>
        /// Is using a secure connection scheme.
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        bool IsSecureConnectionScheme(Uri uri);

        /// <summary>
        /// Get websocket client sender
        /// </summary>
        /// <returns></returns>
        ISender GetSender();

        /// <summary>
        /// Websocket Connection Observable
        /// </summary>
        /// <param name="uri">Websocket Server Endpoint (URI)</param>
        /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
        /// <param name="clientPingTimeSpan">Specific client ping interval. Default is 30 seconds will be used.</param>
        /// <param name="timeout">Specific time out for client trying to connect. Default is 30 seconds.</param>
        /// <returns></returns>
        IObservable<IDataframe> WebsocketConnectObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingTimeSpan = default,
            TimeSpan timeout = default);

        /// <summary>
        /// Websocket Connection Observable with status.
        /// </summary>
        /// <param name="uri">Websocket Server Endpoint (URI)</param>
        /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
        /// <param name="clientPingTimeSpan">Specific client ping interval. Default is 30 seconds will be used.</param>
        /// <param name="timeout">Specific time out for client trying to connect. Default is 30 seconds.</param>
        /// <returns></returns>
        IObservable<(IDataframe dataframe, ConnectionStatus state)> WebsocketConnectWithStatusObservable(
            Uri uri, 
            bool hasClientPing = false,
            TimeSpan clientPingTimeSpan = default, 
            TimeSpan timeout = default);
    }
}
