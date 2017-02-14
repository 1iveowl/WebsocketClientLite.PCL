# Websocket Client Lite 
[![NuGet Badge](https://buildstats.info/nuget/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)

[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v1.2-green.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library)

Note: From version 3.6.0 this library support .NET Core.

For PCL Profile111 compatibility use legacy version 1.6.2:

[![NuGet](https://img.shields.io/badge/nuget-1.6.2_(Profile_111)-yellow.svg)](https://www.nuget.org/packages/WebsocketClientLite.PCL/1.6.2)

## A Light Weigth Cross Platform Websocket Client 

This library is a ground-up implementation of the Websocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455). The implementation does not rely on the build-in Websocket libraries in .NET and UWP etc. 

The library allows developers to establish secure wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care, but is nice to have in testing environments or close local networks IoT set-ups etc. To use this set the ConnectAsync parameter `ignoreServerCertificateErrors: true`.

This project is based on [SocketLite.PCL](https://github.com/1iveowl/SocketLite.PCL) for cross platform TCP sockets support. 

This project utilizes [Reactive Extensions](http://reactivex.io/). Although this has an added learning curve its a learning worth while and it makes creating a library like this much more elegant compared to using call-back or events. 

## Usage
The library is easy to use, which is best illustated with this example:

#### Usings:
```csharp
using ISocketLite.PCL.Model;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;
```

#### Example WebSocket Client:
```csharp
class Program
{
    private static IDisposable _subscribeToMessagesReceived; 
    static void Main(string[] args)
    {
        StartWebSocket();
        System.Console.ReadKey();

		// Don't forget to clean-up with Dispose. It doesn't matter here, but it will in your code.
        _subscribeToMessagesReceived.Dispose();

    }

    static async void StartWebSocket()
    {
        var websocketClient = new MessageWebSocketRx();

		// 1. Start by subscribing to messages. 
        _subscribeToMessagesReceived = websocketClient.ObserveTextMessagesReceived.Subscribe(
            msg =>
            {
				// Your code goes here forhandling an incoming message
                System.Console.WriteLine($"Reply from test server (wss://echo.websocket.org): {msg}");

            });

		// 2a. ### Optional Subprotocols ###
        // The echo.websocket.org does not support any sub-protocols and hence this test does not add any.
        // Adding a sub-protocol that the server does not support causes the client to close down the connection.
		// Anyhow here is how to add 
        // List<string> subprotocols = new List<string> {"soap", "json"};
		List<string> subprotocols = null;

		// 2b. ### Optional headers
		// Adding headers are easy
		var headers = new Dictionary<string, string>{{"Pragma", "no-cache"}, {"Cache-Control", "no-cache"}};

	    // 3. Now establish a connection to the server

		await websocketClient.ConnectAsync(
                new Uri("wss://echo.websocket.org:443"),	// use the publicly available test server: http://www.websocket.org/echo.html
                ignoreServerCertificateErrors: false,		// you can ignore server certificate errors. Good for test, but be careful! 
				headers: headers,
                subprotocols:subprotocols,	
                tlsProtocolVersion:TlsProtocolVersion.Tls12);

		// 4. send a  test to the echo server. It will reply back with what you send. 
        await websocketClient.SendTextAsync("Test Single Frame");

		// 5. you can also send multiple frames in one batch.
        var strArray = new[] {"Test ", "multiple ", "frames"};

        await websocketClient.SendTextAsync(strArray);

		// 6. or you can send frames one by one. Start with FrameType.FirstOfMultipleFrames
        await websocketClient.SendTextMultiFrameAsync("Start ", FrameType.FirstOfMultipleFrames);
        await Task.Delay(TimeSpan.FromMilliseconds(200));

		// ... continue with: FrameType.Continuation
        await websocketClient.SendTextMultiFrameAsync("Continue... #1 ", FrameType.Continuation);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        await websocketClient.SendTextMultiFrameAsync("Continue... #2 ", FrameType.Continuation);
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        await websocketClient.SendTextMultiFrameAsync("Continue... #3 ", FrameType.Continuation);
        await Task.Delay(TimeSpan.FromMilliseconds(400));

		// Don't forget the last stop frame!
        await websocketClient.SendTextMultiFrameAsync("This is the last Stop Frame.", FrameType.LastInMultipleFrames);
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

To futher complicate matters the slack.rtm api also requires at a Slack application layer ping too. A simplified implementation of this could look like this:

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
