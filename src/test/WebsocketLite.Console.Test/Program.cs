using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WebsocketClientLite.PCL;

namespace WebsocketLite.Console.Test
{
    class Program
    {
        private static IDisposable _subscribeToMessagesReceived; 
        static void Main(string[] args)
        {
            StartWebSocket();
            System.Console.ReadKey();

        }

        static async void StartWebSocket()
        {
            var websocketClient = new MessageWebSocketRx();

            _subscribeToMessagesReceived = websocketClient.ObserveTextMessagesReceived.Subscribe(
                msg =>
                {
                    System.Console.WriteLine(msg);

                });

            await
                websocketClient.ConnectAsync(
                    new Uri("wss://echo.websocket.org"),
                    ignoreServerCertificateErrors: false);

            var strArray = new[] {"Test ", "me ", "now"};

            await websocketClient.SendTextAsync(strArray);
        }
    }
}
