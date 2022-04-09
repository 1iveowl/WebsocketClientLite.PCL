using System;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketService : IDisposable
    {
        internal WebsocketConnectionHandler WebsocketConnectHandler { get; private set; }

        public WebsocketService(WebsocketConnectionHandler websocketConnectHandler)
        {
            WebsocketConnectHandler = websocketConnectHandler;
        }

        public void Dispose()
        {
            WebsocketConnectHandler.Dispose();
        }
    }
}
