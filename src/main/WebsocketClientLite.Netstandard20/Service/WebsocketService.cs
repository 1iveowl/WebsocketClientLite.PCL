using System;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketService : IDisposable
    {
        internal WebsocketConnectionHandler WebsocketConnectionHandler { get; private set; }

        public WebsocketService(WebsocketConnectionHandler websocketConnectHandler)
        {
            WebsocketConnectionHandler = websocketConnectHandler;
        }

        public void Dispose()
        {
            WebsocketConnectionHandler.Dispose();
        }
    }
}
