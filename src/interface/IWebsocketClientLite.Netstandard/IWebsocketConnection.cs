using System;

namespace IWebsocketClientLite.PCL
{
    public interface IWebsocketConnection
    {
        IObservable<ConnectionStatus> ConnectionStatusObservable { get; }
        IObservable<string> MessageObservable { get; }
        ISender Sender { get; }
    }
}
