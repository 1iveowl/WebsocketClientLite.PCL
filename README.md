# Websocket Client Lite (Rx)
[![NuGet Badge](https://buildstats.info/nuget/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)

[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.0-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) 

*Please star this project if you find it useful. Thank you.*

## A Light Weight Cross Platform Websocket Client 

This library is a ground-up implementation of the Websocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455). The implementation does not rely on the build-in Websocket libraries in .NET and UWP etc. 

The library allows developers to establish secure wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care, but is nice to have in testing environments or closed local networks IoT set-ups etc. To use this set the ConnectAsync parameter `ignoreServerCertificateErrors: true`.

This project is based on [SocketLite](https://github.com/1iveowl/SocketLite) for cross platform TCP sockets support. 

This project utilizes [Reactive Extensions](http://reactivex.io/). Although this has an added learning curve it is an added learning curve worth while persuing, as it IMHO makes creating a library like this much more elegant compared to using call-back or events. 

## New in version 6.1.
Updates, stability and fundamental improvements to the library. See examples below for changes in usage. 

## New in version 6.0.
Simplifications and no longer relies on SocketLite but utilizes the cross platform capabilities of .NET Standard 2.0 and .NET Core 2.1+.

## New in version 5.0.
From hereon only .NET Standard 2.0 and later are supported.

## Usage
The library is easy to use, as illustated with the examples below.


### Example WebSocket Client:
```csharp
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

            var disposableWebsocketStatus = websocketClient.ConnectionStatusObservable.Subscribe(
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
                    Console.WriteLine($"Connection status error: {ex}.");
                    innerCancellationTokenSource.Cancel();
                },
                () =>
                {
                    Console.WriteLine($"Connection status completed.");
                    innerCancellationTokenSource.Cancel();
                });
            
            var createTokenSource = new CancellationTokenSource();


            var disposableMessageReceiver = websocketClient.MessageReceiverObservable.Subscribe(
               msg =>
               {
                   Console.WriteLine($"Reply from test server: {msg}");
               },
               ex =>
               {
                   Console.WriteLine(ex.Message);
                   innerCancellationTokenSource.Cancel();
               },
               () =>
               {
                   System.Console.WriteLine($"Message listener subscription Completed");
                   innerCancellationTokenSource.Cancel();
               });

            
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

                await websocketClient.DisconnectAsync();

                disposableMessageReceiver.Dispose();
                disposableWebsocketStatus.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                innerCancellationTokenSource.Cancel();
            }
        }
    }

    private static string TestString(int minlength, int maxlength)
    {

        var rng = new Random();

        return RandomStrings(AllowedChars, minlength, maxlength, 25, rng);
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
```

#### Working With Slack (And maybe also other Websocket server implementations)
The [RFC 6455 section defining how ping/pong works](https://tools.ietf.org/html/rfc6455#section-5.5.2) seems to be ambigious on the question of whether or not a pong should include the byte defining the length of "Application Data" in the special case when the length is just zero. 

When testing against [websocket.org](http://websocket.org/echo) the byte is expected and should have the value: 0 (zero). However when used with the [slack.rtm](https://api.slack.com/rtm) api the byte should **not** be there and if it is, the slack websocket server will disconnect.

To manage this *byte-issue* the following property can be set to true, in which case the byte with the zero value will NOT be added to the pong. For instance like this:
```csharp
var websocketClient = new MessageWebSocketRx()
websocketClient.ExcludeZeroApplicationDataInPong = true;
```

To futher complicate matters the [slack.rtm api](https://api.slack.com/rtm) also [seems to requires a ping at the Slack application layer too](http://stackoverflow.com/questions/38171620/slack-rtm-api-disconnection-following-message-in-scala). A simplified implementation of this could look like this:

```csharp
while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    await _webSocket.SendTextAsync("{\"id\": 1234, // ID, see \"sending messages\" above\"type\": \"ping\",...}");
}
```
For details read the **Ping and Pong** section of the [slack.rtm api documentation](https://api.slack.com/rtm) 

#### Monitoring Status
Monitoring connection status is easy: 
```csharp
var websocketLoggerSubscriber = websocketClient.ObserveConnectionStatus.Subscribe(
    status =>
    {
        // Insert code here for logging or handling connection status
        System.Console.WriteLine(status.ToString());
    });
```

#### References:
The following documentation was utilized when writting this library:

 - [RFC 6544](https://tools.ietf.org/html/rfc6455)
 - [Writing Websocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
 - [Writing Websocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
 - [Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)
