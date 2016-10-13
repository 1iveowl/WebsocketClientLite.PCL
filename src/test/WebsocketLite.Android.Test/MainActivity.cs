using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Widget;
using Android.OS;
using ISocketLite.PCL.Model;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;

namespace WebsocketLite.Android.Test
{
    [Activity(Label = "WebsocketLite.Android.Test", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        private static IDisposable _subscribeToMessagesReceived;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            try
            {
                StartWebSocket();
            }
            catch (Exception ex)
            {
                
                throw ex;
            }
            

            //Task.Delay(TimeSpan.FromMinutes(5));

            //_subscribeToMessagesReceived.Dispose();
            // Set our view from the "main" layout resource
            // SetContentView (Resource.Layout.Main);


        }

        private async void StartWebSocket()
        {
            var websocketClient = new MessageWebSocketRx();

            //System.Console.WriteLine("Start");

            _subscribeToMessagesReceived = websocketClient.ObserveTextMessagesReceived.Subscribe(
                msg =>
                {
                    var t = msg;
                    var x = "wait";
                    //System.Console.WriteLine($"Reply from test server (wss://echo.websocket.org): {msg}");
                });

            var cts = new CancellationTokenSource();

            cts.Token.Register(() =>
            {
                //System.Console.Write("Aborted");
                _subscribeToMessagesReceived.Dispose();
            });

            // ### Optional Subprotocols ###
            // The echo.websocket.org does not support any sub-protocols and hence this test does not add any.
            // Adding a sub-protocol that the server does not support causes the client to close down the connection.
            List<string> subprotocols = null; //new List<string> {"soap", "json"};

            try
            {
                await websocketClient.ConnectAsync(
                    new Uri("wss://echo.websocket.org"),
                    cts,
                    subprotocols: subprotocols,
                    ignoreServerCertificateErrors: false,
                    tlsProtocolVersion: TlsProtocolVersion.Tls10);
            }
            catch (Exception ex)
            {
                
                throw ex;
            }
            

            //System.Console.WriteLine("Sending: Test Single Frame");
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
        }
    }
}

