# Websocket Client Lite (Rx)
[![NuGet Badge](https://buildstats.info/nuget/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)

[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.0-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) 
[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.1-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) 

*Please star this project if you find it useful. Thank you.*

## A Lightweight Cross Platform Websocket Client 

This library is a ground-up implementation of the Websocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455). The implementation does not rely on the build-in Websocket libraries in .NET and UWP etc. 

The library allows developers to establish secure wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care, but is useful for testing environments, closed local networks, IoT set-ups etc. To utilize the relaxed security settings set this ConnectAsync parameter: `ignoreServerCertificateErrors: true`.

This project utilizes [Reactive Extensions](http://reactivex.io/). Although this has an added learning curve, it is an added learning curve worthwhile pursuing, as it IMHO makes creating a library like this much more elegant compared to using call-back or events etc. 

## New in version 7.0
This library has been around for a number of years and was mainly initiated on a desire to learn more. Over the years this learning grew and looking back on the older and older code became more and more painful, so I decided to redo it. Version 7 is basically a rewrite from the ground up.

Version 7 supports .NET Standard 2.0 or .NET Standard 2.1.

## New in version 6.4
- Successfully tested with .NET 6.0.
- Previously the library on accepted the `ws` and `wss` scheme. Now also `http` and `https` is supported. To further extend supported scheme override the `IsSecureConnectionScheme` method of the `MessageWebSocketRx` class.

## New in version 6.3
- Fixed bug related to connecting to IPv6 endpoints. 
- Updated System.Reactive to v5.0.0.
- Successfully tested with .NET 5.0.
- Updated Readme.

## New in version 6.1.
Updates, stability and fundamental improvements to the library. See examples below for changes in usage. 

## New in version 6.0.
Simplifications and no longer relies on SocketLite but utilizes the cross-platform capabilities of .NET Standard 2.0 and .NET Core 2.1+.

## New in version 5.0.
From hereon only .NET Standard 2.0 and later are supported.

## Usage
For example please see the [console example app](https://github.com/1iveowl/WebsocketClientLite.PCL/blob/master/src/test/NETCore.Console.Test/Program.cs).

To use the Websocket client create an instance of the class `MessageWebsocketRx`:

```csharp
        var websocketClient = new MessageWebsocketRx()
        {
            IgnoreServerCertificateErrors = true,
            Headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } },
            TlsProtocolType = SslProtocols.Tls12
        };
```



### Alternative Constructor
It is also possible to pass you own managed TcpClient to the WebsocketClientLite. If the TcpClient is not connected the library will connect it. 

To use an existing TcpClient us the alternative constructor use: 
```csharp
MessageWebSocketRx(TcpClient tcpClient)
```

#### Working With Slack (And maybe also other Websocket server implementations)
The [RFC 6455 section defining how ping/pong works](https://tools.ietf.org/html/rfc6455#section-5.5.2) seems to be ambiguous on the question whether or not a pong should include the byte defining the length of "Application Data" in the special case when the length is just zero. 

When testing against [websocket.org](http://websocket.org/echo) the byte is expected and should have the value: 0 (zero). However, when used with the [slack.rtm](https://api.slack.com/rtm) api the byte should **not** be there and if it is, the slack websocket server will disconnect.

To manage this *byte-issue* the following property can be set to true, in which case the byte with the zero value will NOT be added to the pong. For instance like this:
```csharp
var websocketClient = new MessageWebSocketRx()
client.ExcludeZeroApplicationDataInPong = true;
```

To futher complicate matters the [slack.rtm api](https://api.slack.com/rtm) also [seems to requires a ping at the Slack application layer too](http://stackoverflow.com/questions/38171620/slack-rtm-api-disconnection-following-message-in-scala). A simplified implementation of this could look like this:

```csharp
while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    await _webSocket.SendText("{\"id\": 1234, // ID, see \"sending messages\" above\"type\": \"ping\",...}");
}
```
For details read the **Ping and Pong** section of the [slack.rtm api documentation](https://api.slack.com/rtm) 

#### Working with socket.io v4+
The library have been tested with [socket.io](https://socket.io/docs/v4/). A typical connection will look like this:

```csharp
client.ConnectAsync(new Uri($"http://{url}:{port}/socket.io/?EIO=4&transport=websocket");
```

This will connect on the websocket layer. To further connect on socket.io level see documentation. Typically a text message with the content `40` need to be send. For more see here: [Websocket client not connecting to the socket.io server](https://github.com/socketio/socket.io/discussions/4299).


#### References:
The following documentation was utilized when writting this library:

 - [RFC 6544](https://tools.ietf.org/html/rfc6455)
 - [Writing Websocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
 - [Writing Websocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
 - [Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)
