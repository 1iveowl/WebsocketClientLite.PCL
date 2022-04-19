# Websocket Client Lite (Rx)
[![NuGet Badge](https://buildstats.info/nuget/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)

[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.0-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) 
[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.1-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) 

*Please star this project if you find it useful. Thank you.*

## A Lightweight Cross Platform Websocket Client 

This library is a ground-up implementation of the Websocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455) - i.e., this implementation does not rely on the build-in Websocket libraries in .NET.

The library allows developers additional flexibility, including the ability to establish secure wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care for obvious reasons, however it is useful for testing environments, closed local networks, local IoT set-ups etc. To utilize these relaxed security settings set this ConnectAsync parameter: `ignoreServerCertificateErrors: true` or override the `ValidateServerCertificate` method.

Furthermore, this library utilizes [ReactiveX](http://reactivex.io/) (aka Rx or Reactive Extensions). Although taking this dependancy introduces an added learning curve, it is a learning curve worthwhile invsting in, as it IMHO makes using and creating a library like this much more elegant compared to using tranditional call-back or events based patterns etc. 

## New in version 7.0
This library has been around for more than 6 years. It was mainly initiated on a desire to learn and play around with the technologies used. Unsurprisingly, over the years learning and insights did grow, and thus maintaining and looking back on the older code-base, became more and more painful, so I decided to redo it. Version 7 is basically a rewrite of 90+ % of the original code.

Version 7 supports comes in a .NET Standard 2.0 and in a .NET Standard 2.1 flavor.

## New in version 6.4
Successfully tested with .NET 6.0.

Previously the library only accepted the `ws` and `wss` scheme. Now also `http` and `https` is supported. To further extend supported schemes override the `IsSecureConnectionScheme` method of the `MessageWebSocketRx` class.

Default method looks like this:

```csharp
public virtual bool IsSecureConnectionScheme(Uri uri) => 
    uri.Scheme switch
    {
        "ws" or "http" => false,
        "https" or "wss"=> true,
        _ => throw new ArgumentException("Unknown Uri type.")
    };
```

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
From hereon and forward only .NET Standard 2.0 and later are supported.

## Usage
For more deltailed sample of using this library please see the [console example app](https://github.com/1iveowl/WebsocketClientLite.PCL/blob/master/src/test/NETCore.Console.Test/Program.cs).

### To instanciate websocket lite class
To use the Websocket client create an instance of the class `MessageWebsocketRx`:

```csharp
var websocketClient = new MessageWebsocketRx()
{
    IgnoreServerCertificateErrors = false,
    Headers = new Dictionary<string, string> {{ "Pragma", "no-cache" }, { "Cache-Control", "no-cache" }},
    TlsProtocolType = SslProtocols.Tls12
};
```
... or use alternative constructor to pass your own managed TcpClient. 
```csharp
MessageWebSocketRx(TcpClient tcpClient)
```

> Note: If the TcpClient is not connected the library will connect it. Also, the TcpClient will not be disposed automatically when passed in using the constructor, as it will in the case when no TcpClient is supplied.

### To connect client to websocket server

To connect and observe websocket connection use `WebsocketConnectionObservable`:
```csharp
var websocketConnectionObservable = 
    client.WebsocketConnectObservable(
        new Uri(WebsocketTestServerUrl)
);
```
... or use `WebsocketConnectionWithStatusObservable` to also observe connection status :
```csharp
var websocketConnectionWithStatusObservable = 
    client.WebsocketConnectWithStatusObservable(
        new Uri(WebsocketTestServerUrl)
);
```
### To control TLS/SSL certificate validation behavior
To control TLS/SSL Server certificate behavior, either use the `IgnoreServerCertificateErrors` parameter to ignore any issues with the certificate or override the `ValidateServerCertificate` method to your liking. 

The default implementation looks like this:

```csharp
public virtual bool ValidateServerCertificate(
    object senderObject,
    X509Certificate certificate,
    X509Chain chain,
    SslPolicyErrors tlsPolicyErrors)
{
    if (IgnoreServerCertificateErrors) return true;

    return tlsPolicyErrors switch
    {
        SslPolicyErrors.None => true,
        SslPolicyErrors.RemoteCertificateChainErrors => 
            throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateChainErrors}"),
        SslPolicyErrors.RemoteCertificateNameMismatch => 
            throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNameMismatch}"),
        SslPolicyErrors.RemoteCertificateNotAvailable => 
            throw new Exception($"SSL/TLS error: {SslPolicyErrors.RemoteCertificateNotAvailable}"),
        _ => throw new ArgumentOutOfRangeException(nameof(tlsPolicyErrors), tlsPolicyErrors, null),
    };
}
```

### Working With Slack (And maybe also other Websocket server implementations)
The [RFC 6455 section defining how ping/pong works](https://tools.ietf.org/html/rfc6455#section-5.5.2) seems to be ambiguous on the question whether or not a pong should include the byte defining the length of dataframe in the special case when the length is just zero. 

When testing against for instance the Postman Websocket test server [Postman Webocket Server](wss://ws.postman-echo.com/raw) the dataframe byte is expected and should have the value 0 (zero), when there's no data in the dataframe. However, when used with the [slack.rtm](https://api.slack.com/rtm) api the byte should **not** be there at all in the case of no data in the dataframe, and if it is, the slack websocket server will disconnect.

To manage this *byte-issue* the following property can be set to true, in which case the byte with the zero value will NOT be added to the pong. For instance like this:
```csharp
var websocketClient = new MessageWebSocketRx()
client.ExcludeZeroApplicationDataInPong = true;
```

To futher complicate matters the [slack.rtm api](https://api.slack.com/rtm) also [seems to requires a ping at the Slack application layer too](http://stackoverflow.com/questions/38171620/slack-rtm-api-disconnection-following-message-in-scala). A simplified implementation of this could look like this:

```csharp
await _webSocket.SendText("{\"id\": 1234, // ID, see \"sending messages\" above\"type\": \"ping\",...}");
```
For details read the **Ping and Pong** section of the [slack.rtm api documentation](https://api.slack.com/rtm) 

### Working with socket.io v4+
The library have been tested with [socket.io](https://socket.io/docs/v4/). A typical connection will look like this:

```csharp
var websocketConnectionObservable = 
    client.WebsocketConnectWithStatusObservable(
        new Uri($"http://{url}:{port}/socket.io/?EIO=4&transport=websocket"));
```

This will connect on the websocket layer. To further connect on socket.io level see documentation. 

Typically a text message with the content `40` need to be send right after the connection have been established. Also, some socket.io server implementations are very sensitive to the encoding of the messages, and will disconnect immidiately if receiving a dataframe with a text message that does not comply with the socket.io encoding protocol.

For more see here: [Websocket client not connecting to the socket.io server](https://github.com/socketio/socket.io/discussions/4299).


### References:
The following documentation was utilized when writting this library:

 - [RFC 6544](https://tools.ietf.org/html/rfc6455)
 - [Writing Websocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
 - [Writing Websocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
 - [Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)
