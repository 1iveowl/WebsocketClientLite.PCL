using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ISocketLite.PCL.Model;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;

namespace WebsocketLite.Console.Test
{
    class Program
    {
        private static IDisposable _subscribeToMessagesReceived;

        static void Main(string[] args)
        {
   
            StartWebSocketAsync();
            System.Console.WriteLine("Waiting...");
            System.Console.ReadKey();
            _subscribeToMessagesReceived.Dispose();
        }

        private static async void StartWebSocketAsync()
        {
            using (var websocketClient = new MessageWebSocketRx())
            {
                System.Console.WriteLine("Start");

                var websocketMonitorSubscriber = websocketClient.ObserveConnectionStatus.Subscribe(
                    s =>
                    {
                        System.Console.WriteLine(s.ToString());
                    });

                _subscribeToMessagesReceived = websocketClient.ObserveTextMessagesReceived.Subscribe(
                    msg =>
                    {
                        System.Console.WriteLine($"Reply from test server: {msg}");
                    },
                    () =>
                    {
                        System.Console.WriteLine($"Subscription Completed");
                    });

                var cts = new CancellationTokenSource();

                cts.Token.Register(() =>
                {
                    System.Console.Write("Cancelled");
                    //_subscribeToMessagesReceived.Dispose();
                });

                // ### Optional Subprotocols ###
                // The echo.websocket.org does not support any sub-protocols and hence this test does not add any.
                // Adding a sub-protocol that the server does not support causes the client to close down the connection.
                List<string> subprotocols = null; //new List<string> {"soap", "json"};


                await websocketClient.ConnectAsync(
                    new Uri("ws://192.168.0.7:3000/socket.io/?EIO=2&transport=websocket"),
                    //new Uri("wss://echo.websocket.org:443"),
                    //cts,
                    origin: null,
                    ignoreServerCertificateErrors: true,
                    subprotocols: subprotocols,
                    tlsProtocolVersion: TlsProtocolVersion.Tls12);

                //System.Console.WriteLine("Sending: Test Single Frame");
                await websocketClient.SendTextAsync("Test rpi3");


                await websocketClient.CloseAsync();


                await websocketClient.ConnectAsync(
                    //new Uri("ws://localhost:3000/socket.io/?EIO=2&transport=websocket"),
                    new Uri("wss://echo.websocket.org:443"),
                    //cts,
                    ignoreServerCertificateErrors: true,
                    subprotocols: subprotocols,
                    tlsProtocolVersion: TlsProtocolVersion.Tls12);


                System.Console.WriteLine("Sending: Test Single Frame");
                await websocketClient.SendTextAsync("Test Single Frame");


                var strArray = new[] { "Test ", "multiple ", "frames" };

                await websocketClient.SendTextAsync(strArray);

                await websocketClient.SendTextMultiFrameAsync("Start ", FrameType.FirstOfMultipleFrames);
                await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
                await websocketClient.SendTextMultiFrameAsync("Continue... #1 ", FrameType.Continuation);
                await Task.Delay(TimeSpan.FromMilliseconds(300), cts.Token);
                await websocketClient.SendTextMultiFrameAsync("Continue... #2 ", FrameType.Continuation);
                await Task.Delay(TimeSpan.FromMilliseconds(150), cts.Token);
                await websocketClient.SendTextMultiFrameAsync("Continue... #3 ", FrameType.Continuation);
                await Task.Delay(TimeSpan.FromMilliseconds(400), cts.Token);
                await websocketClient.SendTextMultiFrameAsync("Stop.", FrameType.LastInMultipleFrames);

                await websocketClient.CloseAsync();

                await websocketClient.ConnectAsync(
                    new Uri("ws://192.168.0.7:3000/socket.io/?EIO=2&transport=websocket"),
                    //new Uri("wss://echo.websocket.org:443"),
                    //cts,
                    ignoreServerCertificateErrors: true,
                    subprotocols: subprotocols,
                    tlsProtocolVersion: TlsProtocolVersion.Tls12);

                await websocketClient.SendTextAsync("Test localhost");

                await websocketClient.CloseAsync();
            }

            
        }
    }
}
