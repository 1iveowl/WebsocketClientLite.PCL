using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
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
            _subscribeToMessagesReceived.Dispose();

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

            await websocketClient.SendTextMultiFrameAsync("Start ", FrameType.FirstOfMultipleFrames);
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            await websocketClient.SendTextMultiFrameAsync("Continue... #1 ", FrameType.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await websocketClient.SendTextMultiFrameAsync("Continue... #2 ", FrameType.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            await websocketClient.SendTextMultiFrameAsync("Continue... #3 ", FrameType.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            await websocketClient.SendTextMultiFrameAsync("Stop.", FrameType.LastInMultipleFrames);

        }
    }
}
