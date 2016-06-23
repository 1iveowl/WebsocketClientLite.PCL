using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public interface IMessageWebSocketRx
    {
        IObservable<string> ObserveTextMessagesReceived { get; }

        bool IsConnected { get; }

        void SetRequestHeader(string headerName, string headerValue);

        Task ConnectAsync(Uri uri, bool ignoreServerCertificateErrors = false);

        Task CloseAsync();
        //Task CloseAsync(ushort code, string reason);

        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);
    }
}
