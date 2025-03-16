using System;

namespace WebsocketClientLite.Service;

internal class WebsocketService(WebsocketConnectionHandler websocketConnectHandler) : IDisposable
{
    internal WebsocketConnectionHandler WebsocketConnectionHandler { get; private set; } = websocketConnectHandler;

    public void Dispose()
    {
        WebsocketConnectionHandler?.Dispose();
    }
}
