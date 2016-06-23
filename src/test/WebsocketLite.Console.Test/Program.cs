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
                    System.Console.WriteLine($"Reply from test server (wss://echo.websocket.org): {msg}");

                });

            await
                websocketClient.ConnectAsync(
                    new Uri("wss://echo.websocket.org"),
                    ignoreServerCertificateErrors: false);

            await websocketClient.SendTextAsync("Test Single Frame");

            var strArray = new[] {"Test ", "multiple ", "frames"};

            await websocketClient.SendTextAsync(strArray);
        }
    }
}
