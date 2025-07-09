# WebSocket Client Lite (Rx)

[![NuGet Badge](https://img.shields.io/nuget/v/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)
[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.0-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library)[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.1-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library)
[![.NET 6](http://img.shields.io/badge/.NET-v6.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/6.0)[![.NET 8](http://img.shields.io/badge/.NET-v8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)[![.NET 9](http://img.shields.io/badge/.NET-v9.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![System.Reactive](http://img.shields.io/badge/Rx-v6.0.1-ff69b4.svg)](http://reactivex.io/)
![CI/CD](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/workflows/build.yml/badge.svg)

*Please star this project if you find it useful. Thank you.*

## TL;DR

See the project `NETCore.Console.Test` in the `./src/test` directory for an example of how to use.

## A Lightweight Cross Platform WebSocket Client

This library is a ground-up implementation of the WebSocket specification [(RFC 6455)](https://tools.ietf.org/html/rfc6455) - it does not rely on any built-in WebSocket libraries in .NET.

The library provides developers with additional flexibility, including the ability to establish secure WSS websocket connections to servers with self-signing certificates, expired certificates, etc. This capability should be used with care for obvious reasons, but is valuable for testing environments, closed local networks, local IoT set-ups, and more.

The library utilizes [ReactiveX](http://reactivex.io/) (aka Rx or Reactive Extensions). While this dependency introduces a learning curve, it's worthwhile investing in, as it makes library usage and development more elegant than traditional callback or event-based patterns.

## New in Version 8.0

Version 8.0 includes several significant improvements:

- Class name change: Renamed from `MessageWebsocketRx` to `ClientWebSocketRx` to better reflect its purpose.
- Improved TCP socket lifecycle management.
- Enhanced error handling and connection stability.
- Updated to use modern C# features.
- Performance optimizations throughout the codebase.
- Explicit TCP socket lifecycle ownership control.

### TCP Socket Lifecycle Management

Version 8.0 provides better control over the TCP socket lifecycle through the `HasTransferSocketLifeCycleOwnership` property:

```cs
var client = new ClientWebSocketRx
	{
		TcpClient = tcpClient, HasTransferSocketLifeCycleOwnership = true
	};  // When true, the WebSocket client will dispose the TCP client 
```

When set to `true`, the WebSocket client will take ownership of disposing the TCP client when the WebSocket client is disposed.

## Features from Previous Releases

### Client Ping (v7.0)

The client ping feature enables the WebSocket client to send ping messages at predefined intervals:

```csharp
var websocketConnectionObservable = 
    client.WebsocketConnectWithStatusObservable(
        uri: WebsocketServerUri, 
        hasClientPing: true, // default is false. 
        clientPingInterval: TimeSpan.FromSeconds(20), // default is 30 seconds.
        clientPingMessage: "my ping message"); // default no message when set to null.
```

For advanced scenarios, use the `SendPing` method on the `ISender` interface for full control over ping messages.

### HTTP/HTTPS Scheme Support (v6.4)

The library supports `ws`, `wss`, `http`, and `https` URI schemes. You can extend supported schemes by overriding the `IsSecureConnectionScheme` method:

## New in version 6.4

Previously the library only accepted the `ws` and `wss` scheme. Now `http` and `https` is also supported. 

To further extend supported schemes override the `IsSecureConnectionScheme` method of the `MessageWebSocketRx` class.

The default virtual method looks like this:

```csharp
public virtual bool IsSecureConnectionScheme(Uri uri) => 
    uri.Scheme switch
    {
        "ws" or "http" => false,
        "https" or "wss"=> true,
        _ => throw new ArgumentException("Unknown Uri type.")
    };
```

## Usage Guide

### Creating a WebSocket Client

Instantiate the `ClientWebSocketRx` class:

```csharp
var client = new ClientWebSocketRx { 
    IgnoreServerCertificateErrors = true, 
    Headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } },
    TlsProtocolType = SslProtocols.Tls12 };

```
You can also provide your own `TcpClient` for greater control:

```csharp
TcpClient tcpClient = new() { LingerState = new LingerOption(true, 0) };
var client = new ClientWebSocketRx { TcpClient = tcpClient, HasTransferSocketLifeCycleOwnership = false };
```

> Note: 
>
> - If the TcpClient is not connected already the library will connect it. 
> - The TcpClient will not be disposed automatically when passed in using the constructor unless `HasTransferSocketLifeCycleOwnership = true` is set.

### Connecting to a WebSocket Server

To connect and observe WebSocket communication:

```csharp
// Standard connection observable 
IObservable<IDataframe?> websocketObservable = client.WebsocketConnectObservable( 
    uri: new Uri("wss://ws.postman-echo.com/raw"));

// Enhanced connection observable with status information 
IObservable<(IDataframe? dataframe, ConnectionStatus state)> websocketConnectionObservable = client.WebsocketConnectWithStatusObservable( 
    uri: new Uri("wss://ws.postman-echo.com/raw"), 
    hasClientPing: true, 
    clientPingInterval: TimeSpan.FromSeconds(10), 
    clientPingMessage: "ping message" );
```
### Handling Connection Status and Messages

Monitor the connection status and handle incoming messages:

```csharp
IDisposable disposableConnection = websocketConnectionObservable .Do(tuple => { 
    // Handle connection status updates 
    Console.ForegroundColor = 
        (int)tuple.state switch 
    	{ 
            >= 1000 and <= 1999 => ConsoleColor.Magenta, 
           // Connection states 
           >= 2000 and <= 2999 => ConsoleColor.Green,   
           // Control frame states 
           >= 3000 and <= 3999 => ConsoleColor.Cyan,    
           // Data states
           >= 4000 and <= 4999 => ConsoleColor.DarkYellow, 
           // Ping/Pong states 
           _                   => ConsoleColor.Gray, };
    
    Console.WriteLine(tuple.state.ToString());

    if (tuple.state == ConnectionStatus.DataframeReceived && tuple.dataframe is not null)
    {
        Console.WriteLine($"Received: {tuple.dataframe.Message}");
    }
    
    if (tuple.state is ConnectionStatus.Disconnected or 
                    ConnectionStatus.Aborted or 
                    ConnectionStatus.ConnectionFailed)
    {
        // Handle disconnection
    }
})
.Subscribe();
```
### Sending Messages

Once connected, use the WebSocket sender interface to transmit messages:

```cs
// Get the sender 
var sender = client.Sender;

// Send a simple text message 
await sender.SendText("Test Single Frame");

// Send a multi-part message 
await sender.SendText([ "Test ", "multiple ", "frames ", "message." ]);

// Send fragmented messages with control over the fragmentation process 
await sender.SendText("Start ", OpcodeKind.Text, FragmentKind.First); 
await sender.SendText("Continue... ", OpcodeKind.Continuation); 
await sender.SendText("End", OpcodeKind.Text, FragmentKind.Last);
```



### TLS/SSL Certificate Validation

Control certificate validation behavior:

```csharp
// Option 1: Ignore all certificate errors (use with caution) 
var client = new ClientWebSocketRx { IgnoreServerCertificateErrors = true };

// Option 2: Override the validation method for custom logic 
public override bool ValidateServerCertificate(
    object senderObject, X509Certificate certificate, X509Chain chain, SslPolicyErrors tlsPolicyErrors) 
{ 
    // Your custom validation logic here
	// Fall back to base implementation
    return base.ValidateServerCertificate(senderObject, certificate, chain, tlsPolicyErrors);
}
```

### Working with Specialized WebSocket Implementations

#### Slack RTM API

The [RFC 6455 section defining how ping/pong works](https://tools.ietf.org/html/rfc6455#section-5.5.2) seems to be ambiguous on the question whether or not a pong must include the byte defining the length of data-frame, in the special case when there is no data and the length of the data is zero.

When testing against for instance the Postman WebSocket test server [Postman WebSocket Server](wss://ws.postman-echo.com/raw) the data-frame byte is expected and should have the value 0 (zero), when there's no data in the data-frame. 

However, when used with the [slack.rtm](https://api.slack.com/rtm) API the byte should **not** be there at all in the case of no data in the data-frame, and if it is, the slack WebSocket server will disconnect.

To manage this *length byte-issue* the following property can be set to `true`, in which case the byte with the zero value will NOT be added to the pong. For instance like this:

For Slack and similar services with specific websocket requirements:

```csharp
var client = new ClientWebSocketRx { ExcludeZeroApplicationDataInPong = true  // Required for Slack's RTM API };

```

Slack RTM also requires application-level ping messages:

```csharp
await sender.SendText("{"id": 1234, "type": "ping"}");
//or
await _webSocket.SendText("{\"id\": 1234, // ID, see \"sending messages\" above\"type\": \"ping\",...}");
```

To further complicate matters the [slack.rtm api](https://api.slack.com/rtm) [seems to require a ping at the Slack application layer too](http://stackoverflow.com/questions/38171620/slack-rtm-api-disconnection-following-message-in-scala). 

For details read the **Ping and Pong** section of the [slack.rtm API documentation](https://api.slack.com/rtm) 

#### Socket.IO

For Socket.IO servers:

This library has also been tested with [socket.io](https://socket.io/docs/v4/). 

```csharp
var uri = new Uri($"http://{url}:{port}/socket.io/?EIO=4&transport=websocket"); 
var websocketObservable = client.WebsocketConnectWithStatusObservable(uri);
```

This will connect on the WebSocket layer with socket.io server. 

To further connect on socket.io level see documentation. For instance, typically a text message with the content `40` needs to be sent right after the connection have been established. Also, some socket.io server implementations seem to be very sensitive to the encoding of the messages that are being send, and will disconnect immediately if receiving a data-frame with a text message that does not comply with the expected socket.io encoding protocol.

For more see here: [WebSocket client not connecting to the socket.io server](https://github.com/socketio/socket.io/discussions/4299).

## References

This library was developed using the following reference documentation:

- [RFC 6455 - The WebSocket Protocol](https://tools.ietf.org/html/rfc6455)
- [MDN - Writing WebSocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
- [MDN - Writing WebSocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
- [MDN - Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)

## Building and Contributing

This library targets .NET Standard 2.0, .NET Standard 2.1, .NET 6, .NET 8, and .NET 9. The CI/CD pipeline uses GitHub Actions to build, test, and publish packages.

To build locally you need the matching .NET SDKs installed. Some projects target .NET 9.0 which currently requires the preview .NET 9 SDK. If the SDK is not available only the .NET 8 and lower projects can be built.

For contributors and developers, please ensure your changes maintain compatibility with these target frameworks.

## Thank You

Thank you to all the developers who have used this library over the years, reported issues, submitted bug fixes, or made contributions to improve the library. Your feedback and support make open source development rewarding and educational.
