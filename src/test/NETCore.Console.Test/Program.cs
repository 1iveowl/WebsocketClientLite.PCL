using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;

class Program
{

    const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";


    static async Task Main(string[] args)
    {

        var outerCancellationSource = new CancellationTokenSource();

        await StartWebSocketAsyncWithRetry(outerCancellationSource);

        System.Console.WriteLine("Waiting...");
        System.Console.ReadKey();
        outerCancellationSource.Cancel();
    }

    private static async Task StartWebSocketAsyncWithRetry(CancellationTokenSource outerCancellationTokenSource)
    {
        while (!outerCancellationTokenSource.IsCancellationRequested)
        {
            var innerCancellationSource = new CancellationTokenSource();

            await StartWebSocketAsync(innerCancellationSource);

            while (!innerCancellationSource.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), innerCancellationSource.Token);
            }

            // Wait 5 seconds before trying again
            await Task.Delay(TimeSpan.FromSeconds(5), outerCancellationTokenSource.Token);
        }
    }

    private static async Task StartWebSocketAsync(CancellationTokenSource innerCancellationTokenSource)
    {
        using (var websocketClient = new MessageWebSocketRx
        {
            IgnoreServerCertificateErrors = true,
            Headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } },
            TlsProtocolType = SslProtocols.Tls12
             
        })
        {
            System.Console.WriteLine("Start");

            var websocketLoggerSubscriber = websocketClient.ConnectionStatusObservable.Subscribe(
                s =>
                {
                    System.Console.WriteLine(s.ToString());
                    if (s == ConnectionStatus.Disconnected
                    || s == ConnectionStatus.Aborted
                    || s == ConnectionStatus.ConnectionFailed)
                    {
                        innerCancellationTokenSource.Cancel();
                    }
                },
                ex =>
                {
                    innerCancellationTokenSource.Cancel();
                },
                () =>
                {
                    innerCancellationTokenSource.Cancel();
                });
            
            var createTokenSource = new CancellationTokenSource();


            var subscribeToMessagesReceived = websocketClient.MessageReceiverObservable.Subscribe(
               msg =>
               {
                   System.Console.WriteLine($"Reply from test server: {msg}");
               },
               ex =>
               {
                   System.Console.WriteLine(ex.Message);
                   innerCancellationTokenSource.Cancel();
               },
               () =>
               {
                   System.Console.WriteLine($"Subscription Completed");
                   innerCancellationTokenSource.Cancel();
               });

            
            await websocketClient.ConnectAsync(
                new Uri("wss://echo.websocket.org"), createTokenSource.Token);



            await Task.Delay(TimeSpan.FromSeconds(1), createTokenSource.Token);

            await websocketClient.DisconnectAsync();

            await Task.Delay(TimeSpan.FromSeconds(5000), createTokenSource.Token);

            await websocketClient.ConnectAsync(
                new Uri("wss://echo.websocket.org"), createTokenSource.Token);

            try
            {
                System.Console.WriteLine("Sending: Test Single Frame");
                await websocketClient.SendTextAsync("Test Single Frame");

                await websocketClient.SendTextAsync("Test Single Frame again");

                await websocketClient.SendTextAsync(TestString(65538, 65550));

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

                // Close the Websocket connection gracefully telling the server goodbye
                //await websocketClient.DisconnectAsync();

                //subscribeToMessagesReceived.Dispose();
                //websocketLoggerSubscriber.Dispose();

                await Task.Delay(TimeSpan.FromSeconds(300), createTokenSource.Token);

                await websocketClient.SendTextAsync(TestString(65538, 65550));

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
            catch (Exception e)
            {
                Console.WriteLine(e);
                innerCancellationTokenSource.Cancel();
            }
        }
    }

    private static string TestString(int minlength, int maxlenght)
    {

        var rng = new Random();

        return RandomStrings(AllowedChars, minlength, maxlenght, 25, rng);
    }

    private static string RandomStrings(
        string allowedChars,
        int minLength,
        int maxLength,
        int count,
        Random rng)
    {
        var chars = new char[maxLength];
        var setLength = allowedChars.Length;

        var length = rng.Next(minLength, maxLength + 1);

        for (var i = 0; i < length; ++i)
        {
            chars[i] = allowedChars[rng.Next(setLength)];
        }

        return new string(chars, 0, length);
    }
}