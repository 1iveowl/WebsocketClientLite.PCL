using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IWebsocketClientLite.PCL
{
    /// <summary>
    /// Websocket Client Lite
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
        string? Origin { get; }

        /// <summary>
        /// Http Headers
        /// </summary>
        IDictionary<string, string>? Headers { get; }

        /// <summary>
        /// Websocket known subprotocols
        /// </summary>
        IEnumerable<string>? Subprotocols { get; }

        /// <summary>
        /// TLS protocol
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
        X509CertificateCollection? X509CertCollection { get; }

        /// <summary>
        /// Server certificate validation method.
        /// </summary>
        /// <param name="senderObject">Sender object</param>
        /// <param name="certificate">X.509 Certificate</param>
        /// <param name="chain">X.509 Chain</param>
        /// <param name="TlsPolicyErrors"> TLS/SSL policy Errors</param>
        /// <returns></returns>
        bool ValidateServerCertificate(
            object senderObject,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors TlsPolicyErrors);

        /// <summary>
        /// Is using a secure connection scheme method.
        /// </summary>
        /// <param name="uri">Secure connection scheme method.</param>
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
        /// <param name="clientPingInterval">Specific client ping interval. Default is 30 seconds.</param>
        /// <param name="clientPingMessage">Specific client message. Default none. Will stay constant and can only be a <see langword="string"/>. For more advanced scenarios use <see cref="ISender.SendPing"/></param>
        /// <param name="handshaketimeout">Specific time-out for client trying to connect (aka handshake). Default is 30 seconds.</param>
        /// <returns></returns>
        IObservable<IDataframe?> WebsocketConnectObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingInterval = default,
            string? clientPingMessage = default,
            TimeSpan handshaketimeout = default);

        /// <summary>
        /// Websocket Connection Observable with status.
        /// </summary>
        /// <param name="uri">Websocket Server Endpoint (URI)</param>
        /// <param name="hasClientPing">Set to true to have the client send ping messages to server.</param>
        /// <param name="clientPingInterval">Specific client ping interval. Default is 30 seconds.</param>
        /// <param name="clientPingMessage">Specific client message. Default none. Will stay constant and can only be a <see langword="string"/>. For more advanced scenarios use <see cref="ISender.SendPing"/></param>
        /// <param name="handshaketimeout">Specific time-out for client trying to connect (aka handshake). Default is 30 seconds.</param>
        /// <returns></returns>
        IObservable<(IDataframe? dataframe, ConnectionStatus state)> WebsocketConnectWithStatusObservable(
            Uri uri,
            bool hasClientPing = false,
            TimeSpan clientPingInterval = default,
            string? clientPingMessage = default,
            TimeSpan handshaketimeout = default);
    }
}

