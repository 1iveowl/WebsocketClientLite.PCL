using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ISocketLite.PCL.Model;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;

class Program
{

    const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private static bool _isConnected;

    static void Main(string[] args)
    {

        var outerCancellationSource = new CancellationTokenSource();

        Task.Run(() => StartWebSocketAsyncWithRetry(outerCancellationSource), outerCancellationSource.Token);

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
        using (var websocketClient = new MessageWebSocketRx())
        {
            System.Console.WriteLine("Start");

            var websocketLoggerSubscriber = websocketClient.ObserveConnectionStatus.Subscribe(
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

            List<string> subprotocols = null; //new List<string> {"soap", "json"};

            var headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } };

            var messageObserver = await websocketClient.CreateObservableMessageReceiver(
                new Uri("wss://spc.1iveowl.dk:8088/ws/spc/?username=WS_usr&password=SpecialPostTjenesten_ws"),
                ignoreServerCertificateErrors: true,
                headers: headers,
                subProtocols: subprotocols,
                tlsProtocolType: TlsProtocolVersion.Tls12);

             var subscribeToMessagesReceived = messageObserver.Subscribe(
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
                await websocketClient.CloseAsync();

                subscribeToMessagesReceived.Dispose();
                websocketLoggerSubscriber.Dispose();
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