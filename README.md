# Websocket Client Lite 
### A light weigth websocket client for Xamarin Forms on .NET 4.5+, Windows 10/UWP, iOS and Android that utilizes Reactive Extensions (Rx)

This library was written to make it easy to use Websocket across all the major platforms platforms.

This library is a ground-up implementation of the Websocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455). The implementation does not rely on the build in Websocket libraries in .NET and UWP. 

The library allows developers to establish wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care, but is nice to have in testing environments or close local networks IoT set-ups etc. To use this set the ConnectAsync parameter `ignoreServerCertificateErrors: true`.

This project is based on [SocketLite.PCL](https://github.com/1iveowl/sockets-for-pcl/) for cross platform TCP sockets support. 

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

        await
            websocketClient.ConnectAsync(
                new Uri("wss://echo.websocket.org"),
                ignoreServerCertificateErrors: false);

        await websocketClient.SendTextAsync("Test Single Frame");

        var strArray = new[] {"Test ", "multiple ", "frames"};

        await websocketClient.SendTextAsync(strArray);
    }
}
```


####References:
The following documentation was utilized when writting this library:

[RFC 6544](https://tools.ietf.org/html/rfc6455)
[Writing Websocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
[Writing Websocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
[Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)

