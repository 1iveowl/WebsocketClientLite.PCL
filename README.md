# Websocket Client Lite (Rx)
[![NuGet Badge](https://buildstats.info/nuget/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)

[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.0-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) 

Note: From version 3.6.0 this library support .NET Core.

For PCL Profile111 compatibility use legacy version 1.6.2:

[![NuGet](https://img.shields.io/badge/nuget-1.6.2_(Profile_111)-yellow.svg)](https://www.nuget.org/packages/WebsocketClientLite.PCL/1.6.2)

*Please star this project if you find it useful. Thank you.*

## A Light Weight Cross Platform Websocket Client 

This library is a ground-up implementation of the Websocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455). The implementation does not rely on the build-in Websocket libraries in .NET and UWP etc. 

The library allows developers to establish secure wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care, but is nice to have in testing environments or close local networks IoT set-ups etc. To use this set the ConnectAsync parameter `ignoreServerCertificateErrors: true`.

This project is based on [SocketLite.PCL](https://github.com/1iveowl/SocketLite.PCL) for cross platform TCP sockets support. 

This project utilizes [Reactive Extensions](http://reactivex.io/). Although this has an added learning curve its a learning worth while and it makes creating a library like this much more elegant compared to using call-back or events. 

## New in version 6.0.
Simplifications and no longer relies on SocketLite but utilizes the cross platform capabilities of .NET Standard 2.0.

## New in version 5.0.
From hereon only .NET Standard 2.0 and later are supported.

## New in Version 4.0
Version 4.0 represents a major overhaul. Unfortunately version 4.0 is **not** backwards compatible with the previous version. There were just to many things I wanted to change. That said, version 4.0 does not have any new functionality, so if you don't want to upgrade you don't have to - at least not in the short term. Version 4.0 is a bit fast, and future versions might add new functionality, so I do recommend the small effort involved in upgrading your code to version 4.0.

On the of changes in version 4.0 is that it is no longer required to do a `ConnectAsync`. All you have to do is to subscribe to the observable and you are set. I still recommened that you do a `CloseAsync` to close the connection to the server gracefully. After closing the websocket connection, you should dispose of your subscrptions. Also to reconnect you should create a new `MessageWebSocketRx`object. 

## Usage
The library is easy to use, as illustated with the examples below.


### Example WebSocket Client:
```csharp
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
			
			var createTokenSource = new CancellationTokenSource();

            var messageObserver = await websocketClient.CreateObservableMessageReceiver(
                new Uri("wss://echo.websocket.org"),
                ignoreServerCertificateErrors: true,
                headers: headers,
                subProtocols: subprotocols,
                tlsProtocolType: SslProtocols.Tls12, 
                token: createTokenSource.Token);

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
```

#### Working With Slack (And Maybe Also Other Websocket Server Implementations)
The [RFC 6455 section defining how ping/pong works](https://tools.ietf.org/html/rfc6455#section-5.5.2) seem to be ambigious on the question of whether or not a pong should include the byte defining the length of "Application Data" when the length is zero. 

When testing against [websocket.org](http://websocket.org/echo) the byte is expected with the value of zero, however when used with the [slack.rtm](https://api.slack.com/rtm) api the byte should not be there or the slack websocket server will disconnect.

To manage this byte the following connect parameter can be set to true. Like this:
```csharp
await _webSocket.ConnectAsync(_uri, excludeZeroApplicationDataInPong:true);
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

####References:
The following documentation was utilized when writting this library:

 - [RFC 6544](https://tools.ietf.org/html/rfc6455)
 - [Writing Websocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
 - [Writing Websocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
 - [Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)
