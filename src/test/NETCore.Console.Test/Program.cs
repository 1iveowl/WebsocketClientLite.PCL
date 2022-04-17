using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;

class Program
{
    const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    const string WebsocketTestServerUrl = "wss://ws.postman-echo.com/raw";
    //const string WebsocketTestServerUrl = "wss://ws.ifelse.io";
    //const string WebsocketTestServerUrl = "http://ubuntusrv2.my.home:3000/socket.io/?EIO=4&transport=websocket";
    //const string WebsocketTestServerUrl = "172.19.128.84:3000";
    //const string WebsocketTestServerUrl = "localhost:3000";

    static async Task Main()
    {

        var outerCancellationSource = new CancellationTokenSource();

        await StartWebSocketAsyncWithRetry(outerCancellationSource);

        Console.WriteLine("Waiting...");
        Console.ReadKey();
        outerCancellationSource.Cancel();
    }

    private static async Task StartWebSocketAsyncWithRetry(CancellationTokenSource outerCancellationTokenSource)
    {
        var tcpClient = new TcpClient { LingerState = new LingerOption(true, 0) };


        while (!outerCancellationTokenSource.IsCancellationRequested)
        {
            var innerCancellationSource = new CancellationTokenSource();

            await StartWebSocketAsync(tcpClient, innerCancellationSource);

            while (!innerCancellationSource.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), innerCancellationSource.Token);
            }

            // Wait 5 seconds before trying again
            await Task.Delay(TimeSpan.FromSeconds(5), outerCancellationTokenSource.Token);
        }
    }

    private static async Task StartWebSocketAsync(
        TcpClient tcpClient,
        CancellationTokenSource innerCancellationTokenSource)
    {
        var client = new MessageWebsocketRx(tcpClient)
        {
            IgnoreServerCertificateErrors = true,
            Headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } },
            TlsProtocolType = SslProtocols.Tls12
        };

        Console.WriteLine("Start");

        IObservable<(IDataframe dataframe, ConnectionStatus state)> websocketConnectionObservable = 
            client.WebsocketConnectWithStatusObservable(
               new Uri(WebsocketTestServerUrl), 
               hasClientPing: false,
               clientPingTimeSpan: TimeSpan.FromSeconds(10));

        var disposableConnectionStatus = websocketConnectionObservable
            .Do(tuple =>
            {
                Console.ForegroundColor = (int)tuple.state switch
                {
                    >= 1000 and <= 1999 => ConsoleColor.Magenta,
                    >= 2000 and <= 2999 => ConsoleColor.Green,
                    >= 3000 and <= 3999 => ConsoleColor.Cyan,
                    >= 4000 and <= 4999 => ConsoleColor.DarkYellow,
                    _                   => ConsoleColor.Gray,
                };              

                Console.WriteLine(tuple.state.ToString() );

                if (tuple.state == ConnectionStatus.Disconnected
                || tuple.state == ConnectionStatus.Aborted
                || tuple.state == ConnectionStatus.ConnectionFailed)
                {
                    innerCancellationTokenSource.Cancel();
                }

                if (tuple.state == ConnectionStatus.DataframeReceived 
                    && tuple.dataframe is not null)
                {
                    Console.WriteLine($"Echo: {tuple.dataframe.Message}");
                }
            })
            .Where(tuple => tuple.state == ConnectionStatus.WebsocketConnected)
            .Select(_ => Observable.FromAsync(_ => SendTest1()))
            .Concat()
            .Select(_ => Observable.FromAsync(_ => SendTest2()))
            .Concat()
            .Subscribe(
            _ => { },
            ex =>
            {
                Console.WriteLine($"Connection status error: {ex}.");
                innerCancellationTokenSource.Cancel();
            },
            () =>
            {
                Console.WriteLine($"Connection status completed.");
                innerCancellationTokenSource.Cancel();
            });

        await Task.Delay(TimeSpan.FromSeconds(200));

        async Task SendTest1()
        {
            var sender = client.GetSender();

            await Task.Delay(TimeSpan.FromSeconds(4));

            await sender.SendText("Test Single Frame 1");

            await Task.Delay(TimeSpan.FromSeconds(5));

            await sender.SendText("Test Single Frame 2");

            await sender.SendText("Test Single Frame again");

            await sender.SendText(TestString(1026, 1026));

            //await Task.Delay(TimeSpan.FromSeconds(2));

            await sender.SendText(new[] { "Test ", "multiple ", "frames ", "1 ", "2 ", "3 ", "4 ", "5 ", "6 ", "7 ", "8 ", "9 " });

            await sender.SendText("Start ", OpcodeKind.Text, FragmentKind.First);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText("Continue... #1 ", OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText("Continue... #2 ", OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(550));
            await sender.SendText("Continue... #3 ", OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText("Stop.", OpcodeKind.Text, FragmentKind.Last);           

            await Task.Delay(TimeSpan.FromSeconds(20));
        }

        async Task SendTest2()
        {
            var sender = client.GetSender();

            Console.WriteLine("Sending: Test Single Frame");
            await sender.SendText("Test Single Frame");

            await sender.SendText("Test Single Frame again");

            await sender.SendText(TestString(1024, 1024));

            await sender.SendText(new[] { "Test ", "multiple ", "frames" });

            await sender.SendText("Start ", OpcodeKind.Text, FragmentKind.First);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText("Continue... #1 ", OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText("Continue... #2 ", OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(550));
            await sender.SendText("Continue... #3 ", OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText("Stop.", OpcodeKind.Text, FragmentKind.Last);

            await Task.Delay(TimeSpan.FromSeconds(20));
        }        
    }

    private static string TestString(int minlength, int maxlength)
    {

        var rng = new Random();

        return RandomStrings(AllowedChars, minlength, maxlength, rng);
    }

    private static string RandomStrings(
        string allowedChars,
        int minLength,
        int maxLength,
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