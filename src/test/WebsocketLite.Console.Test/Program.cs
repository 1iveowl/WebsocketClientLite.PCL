using System;
using System.Collections.Generic;
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

            // This example is out-dated. See the NETCore.Console.Test for the latest version of the example
   
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

                var websocketLoggerSubscriber = websocketClient.ObserveConnectionStatus.Subscribe(
                    s =>
                    {
                        System.Console.WriteLine(s.ToString());
                    });

        _subscribeToMessagesReceived = websocketClient.ObserveTextMessagesReceived.Subscribe(
            msg =>
            {
                System.Console.WriteLine($"Reply from test server: {msg}");
            },
            ex =>
            {
                System.Console.WriteLine(ex.Message);
            },
            () =>
            {
                System.Console.WriteLine($"Subscription Completed");
            });

                // ### Optional Subprotocols ###
                // The echo.websocket.org does not support any sub-protocols and hence this test does not add any.
                // Adding a sub-protocol that the server does not support causes the client to close down the connection.
                List<string> subprotocols = null; //new List<string> {"soap", "json"};


                //await websocketClient.ConnectAsync(
                //    new Uri("ws://192.168.0.7:3000/socket.io/?EIO=2&transport=websocket"),
                //    //new Uri("wss://echo.websocket.org:443"),
                //    //cts,
                //    origin: null,
                //    ignoreServerCertificateErrors: true,
                //    subprotocols: subprotocols,
                //    tlsProtocolVersion: TlsProtocolVersion.Tls12);

                ////System.Console.WriteLine("Sending: Test Single Frame");
                //await websocketClient.SendTextAsync("Test rpi3");


                //await websocketClient.CloseAsync();


                await websocketClient.ConnectAsync(
                    //new Uri("ws://localhost:3000/socket.io/?EIO=2&transport=websocket"),
                    new Uri("wss://echo.websocket.org:443"),
                    //cts,
                    ignoreServerCertificateErrors: true,
                    subprotocols: subprotocols,
                    tlsProtocolVersion: TlsProtocolVersion.Tls12);

                System.Console.WriteLine("Waiting 20 seconds to send");
                await Task.Delay(TimeSpan.FromSeconds(20));

                System.Console.WriteLine("Sending: Test Single Frame");
                try
                {
                    await websocketClient.SendTextAsync("Test Single Frame");
                }
                catch (Exception e)
                {
                    System.Console.WriteLine(e);
                    //throw;
                }
                


                var strArray = new[] { "Test ", "multiple ", "frames" };

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

                System.Console.WriteLine("Press a key and send more data");
                System.Console.ReadKey();
                await websocketClient.SendTextAsync("Test yet another Single Frame");

                System.Console.WriteLine("Waiting for 10 minutes");

                await Task.Delay(TimeSpan.FromMinutes(10));

                await websocketClient.CloseAsync();

                //await websocketClient.ConnectAsync(
                //    new Uri("ws://192.168.0.7:3000/socket.io/?EIO=2&transport=websocket"),
                //    //new Uri("wss://echo.websocket.org:443"),
                //    //cts,
                //    ignoreServerCertificateErrors: true,
                //    subprotocols: subprotocols,
                //    tlsProtocolVersion: TlsProtocolVersion.Tls12);

                //await websocketClient.SendTextAsync("Test localhost");

                //await websocketClient.CloseAsync();
            }
        }
    }
}
