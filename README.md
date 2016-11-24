
# Websocket Client Lite 
[![NuGet](https://img.shields.io/badge/nuget-2.0.11_(.Net_Standard_1.2)-brightgreen.svg)](https://www.nuget.org/packages/WebsocketClientLite.PCL/2.0.11)
[![NuGet](https://img.shields.io/badge/nuget-1.6.2_(Profile_111)-yellow.svg)](https://www.nuget.org/packages/WebsocketClientLite.PCL/1.6.2)
### A light weigth websocket client for Xamarin Forms on .NET 4.5+, Windows 10/UWP, iOS and Android that utilizes Reactive Extensions (Rx)

This library was written to make it easy to use Websocket across all the major platforms platforms.

This library is a ground-up implementation of the Websocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455). The implementation does not rely on the build-in Websocket libraries in .NET and UWP. 

The library allows developers to establish secure wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care, but is nice to have in testing environments or close local networks IoT set-ups etc. To use this set the ConnectAsync parameter `ignoreServerCertificateErrors: true`.

This project is based on [SocketLite.PCL](https://github.com/1iveowl/SocketLite.PCL) for cross platform TCP sockets support. 

This project utilizes [Reactive Extensions](http://reactivex.io/). Although this has an added learning curve its a learning worth while and it makes creating a library like this much more elegant compared to using call-back or events. 

### Usage
```csharp
class Program
{
    private static IDisposable _subscribeToMessagesReceived; 
    static void Main(string[] args)
    {
        StartWebSocket();
        System.Console.ReadKey();
        _subscribeToMessagesReceived.Dispose();

    }

    static async void StartWebSocket()
    {
        var websocketClient = new MessageWebSocketRx();

        _subscribeToMessagesReceived = websocketClient.ObserveTextMessagesReceived.Subscribe(
            msg =>
            {
                System.Console.WriteLine($"Reply from test server (wss://echo.websocket.org): {msg}");

            });

        var cts = new CancellationTokenSource();

        cts.Token.Register(() =>
        {
            System.Console.Write("Aborted. Connection cancelled by server or server became unavailable.");
            _subscribeToMessagesReceived.Dispose();
        });
        
        // ### Optional Subprotocols ###
        // The echo.websocket.org does not support any sub-protocols and hence this test does not add any.
        // Adding a sub-protocol that the server does not support causes the client to close down the connection.
        List<string> subprotocols = null; //new List<string> {"soap", "json"};

        await
            websocketClient.ConnectAsync(
                new Uri("wss://echo.websocket.org:443"),
                cts,
                ignoreServerCertificateErrors: false,
                subprotocols:subprotocols, 
                tlsProtocolVersion:TlsProtocolVersion.Tls12);

        await websocketClient.SendTextAsync("Test Single Frame");

        var strArray = new[] {"Test ", "multiple ", "frames"};

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
}
```


####References:
The following documentation was utilized when writting this library:

 - [RFC 6544](https://tools.ietf.org/html/rfc6455)
 - [Writing Websocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
 - [Writing Websocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
 - [Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)
