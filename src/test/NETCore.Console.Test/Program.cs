using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite;

class Program
{
    private static Func<string, string> _socketIOMessageFormattingFunc;

    const string _allowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    const string _websocketTestServerUrl = "wss://ws.postman-echo.com/raw";
    //const string WebsocketTestServerUrl = "wss://ws.ifelse.io";
    //const string WebsocketTestServerUrl = "http://ubuntusrv2.my.home:3000/socket.io/?EIO=4&transport=websocket";
    //const string WebsocketTestServerUrl = "http://ubuntusrv2.my.home:3030/socket.io/?EIO=4&transport=websocket";
    //const string WebsocketTestServerUrl = "172.19.128.84:3000";
    //const string WebsocketTestServerUrl = "localhost:3000";

    static async Task Main()
    {
        CancellationTokenSource outerCancellationSource = new();

        await StartWebSocketAsyncWithRetry(outerCancellationSource);

        Console.WriteLine("Waiting...");
        Console.ReadKey();
        outerCancellationSource.Cancel();
    }

private static async Task StartWebSocketAsyncWithRetry(
    CancellationTokenSource outerCancellationTokenSource,
    bool isSocketIOTest = false)
{
    if (isSocketIOTest)
    {
        _socketIOMessageFormattingFunc = msg => $"42[\"message\", \"{msg}\"]";
    }
    else
    {
        _socketIOMessageFormattingFunc = msg => msg;
    }

    TcpClient tcpClient = new() { LingerState = new LingerOption(true, 0) };

    var client = new ClientWebSocketRx
    {
        TcpClient = tcpClient,
        HasTransferSocketLifeCycleOwnership = false,
        IgnoreServerCertificateErrors = true,
        Headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } },
        TlsProtocolType = SslProtocols.Tls12,
    };

    using CompositeDisposable disposables = new();

    IDisposable isConnectedDisposable = client.IsConnectedObservable
        .Do(isConnected =>
        {
            Console.WriteLine($"*** Is connected: {isConnected} ***");
        })
        .Subscribe();
    disposables.Add(isConnectedDisposable);

    Func<IObservable<(IDataframe dataframe, ConnectionStatus state)>> connect = () =>
        client.WebsocketConnectWithStatusObservable(
            uri: new Uri(_websocketTestServerUrl),
            hasClientPing: true,
            clientPingInterval: TimeSpan.FromSeconds(10),
            clientPingMessage: "ping message",
            cancellationToken: outerCancellationTokenSource.Token);

    IDisposable disposableConnectionStatus = Observable.Defer(connect)
        .Retry()
        .Repeat()
        .DelaySubscription(TimeSpan.FromSeconds(5))
        .Do(tuple =>
        {
            Console.ForegroundColor = (int)tuple.state switch
            {
                >= 1000 and <= 1999 => ConsoleColor.Magenta,
                >= 2000 and <= 2999 => ConsoleColor.Green,
                >= 3000 and <= 3999 => ConsoleColor.Cyan,
                >= 4000 and <= 4999 => ConsoleColor.DarkYellow,
                _ => ConsoleColor.Gray,
            };

            Console.WriteLine(tuple.state.ToString());

            if (tuple.state == ConnectionStatus.DataframeReceived && tuple.dataframe is not null)
            {
                Console.WriteLine($"Echo: {tuple.dataframe.Message}");
            }
        })
        .Where(tuple => tuple.state == ConnectionStatus.WebsocketConnected)
        .SelectMany(_ => Observable.FromAsync(_ => SendTest1()))
        .SelectMany(_ => Observable.FromAsync(_ => SendTest2()))
        .Subscribe(
            _ => { },
            ex => Console.WriteLine($"Connection status error: {ex}."),
            () => Console.WriteLine($"Connection status completed."));

    disposables.Add(disposableConnectionStatus);

    try
    {
        await Task.Delay(Timeout.Infinite, outerCancellationTokenSource.Token);
    }
    finally
    {
        disposables.Dispose();
    }

        async Task SendTest1(bool isSocketIOTest = false)
        {
            var sender = client.Sender;

            if (sender is null)
            {
                Console.WriteLine("Sender is null.");
                return;
            }

            if(isSocketIOTest)
            {
                await sender.SendText("40");
            }

            await Task.Delay(TimeSpan.FromSeconds(4));

            await sender.SendText(_socketIOMessageFormattingFunc("Test Single Frame 1"));

            await Task.Delay(TimeSpan.FromSeconds(5));

            await sender.SendText(_socketIOMessageFormattingFunc("Test Single Frame 2"));

            await sender.SendText(_socketIOMessageFormattingFunc("Test Single Frame again"));

            await Task.Delay(TimeSpan.FromSeconds(5));

            await sender.SendText(_socketIOMessageFormattingFunc(TestString(1026, 1026)));

            await sender.SendText(new[] 
            {
                _socketIOMessageFormattingFunc("Test "),
                _socketIOMessageFormattingFunc("multiple "),
                _socketIOMessageFormattingFunc("frames "),
                _socketIOMessageFormattingFunc("1 "),
                _socketIOMessageFormattingFunc("2 "),
                _socketIOMessageFormattingFunc("3 "),
                _socketIOMessageFormattingFunc("4 "),
                _socketIOMessageFormattingFunc("5 "),
                _socketIOMessageFormattingFunc("6 "),
                _socketIOMessageFormattingFunc("7 "),
                _socketIOMessageFormattingFunc("8 "),
                _socketIOMessageFormattingFunc("9.")});

            await sender.SendText(_socketIOMessageFormattingFunc("Start "), OpcodeKind.Text, FragmentKind.First);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText(_socketIOMessageFormattingFunc("Continue... #1 "), OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText(_socketIOMessageFormattingFunc("Continue... #2 "), OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(550));
            await sender.SendText(_socketIOMessageFormattingFunc("Continue... #3 "), OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText(_socketIOMessageFormattingFunc("Stop."), OpcodeKind.Text, FragmentKind.Last);           

            await Task.Delay(TimeSpan.FromSeconds(20));
        }

        async Task SendTest2()
        {
            ISender sender = client.Sender;

            if (sender is null)
            {
                Console.WriteLine("Sender is null.");
                return;
            }

            await sender.SendText(_socketIOMessageFormattingFunc("Test Single Frame 1"));

            await Task.Delay(TimeSpan.FromSeconds(5));

            await sender.SendText(_socketIOMessageFormattingFunc("Test Single Frame 2"));

            await sender.SendText(_socketIOMessageFormattingFunc("Test Single Frame again"));

            await Task.Delay(TimeSpan.FromSeconds(5));

            await sender.SendText(_socketIOMessageFormattingFunc(TestString(1026, 1026)));

            await sender.SendText(
            [
                _socketIOMessageFormattingFunc("Test "),
                _socketIOMessageFormattingFunc("multiple "),
                _socketIOMessageFormattingFunc("frames "),
                _socketIOMessageFormattingFunc("1 "),
                _socketIOMessageFormattingFunc("2 "),
                _socketIOMessageFormattingFunc("3 "),
                _socketIOMessageFormattingFunc("4 "),
                _socketIOMessageFormattingFunc("5 "),
                _socketIOMessageFormattingFunc("6 "),
                _socketIOMessageFormattingFunc("7 "),
                _socketIOMessageFormattingFunc("8 "),
                _socketIOMessageFormattingFunc("9.")]);

            await sender.SendText(_socketIOMessageFormattingFunc("Start "), OpcodeKind.Text, FragmentKind.First);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText(_socketIOMessageFormattingFunc("Continue... #1 "), OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText(_socketIOMessageFormattingFunc("Continue... #2 "), OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(550));
            await sender.SendText(_socketIOMessageFormattingFunc("Continue... #3 "), OpcodeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendText(_socketIOMessageFormattingFunc("Stop."), OpcodeKind.Text, FragmentKind.Last);

            await Task.Delay(TimeSpan.FromSeconds(20));
        }
    }



    private static string TestString(int minlength, int maxlength)
    {

        Random rng = new();

        return RandomStrings(_allowedChars, minlength, maxlength, rng);
    }

    private static string RandomStrings(
        string allowedChars,
        int minLength,
        int maxLength,
        Random rng)
    {
        char[] chars = new char[maxLength];
        int setLength = allowedChars.Length;

        int length = rng.Next(minLength, maxLength + 1);

        for (int i = 0; i < length; ++i)
        {
            chars[i] = allowedChars[rng.Next(setLength)];
        }

        return new string(chars, 0, length);
    }
}

