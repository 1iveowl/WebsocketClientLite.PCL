using IWebsocketClientLite.PCL;
using System;
namespace WebsocketClientLite.PCL.Model
{
    public record WebsocketConnection : IWebsocketConnection
    {
        public IObservable<ConnectionStatus> ConnectionStatusObservable {get; init;}

        public IObservable<string> MessageObservable { get; init; }

        public ISender Sender {get; init;}
    }
}
