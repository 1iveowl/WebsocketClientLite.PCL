# WebSocket Client Lite (Rx)
[![NuGet Badge](https://buildstats.info/nuget/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)

[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.0-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) [![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.1-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library) [![System.Reactive](http://img.shields.io/badge/Rx-v5.0.0-ff69b4.svg)](http://reactivex.io/)

![CI/CD](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/workflows/build.yml/badge.svg)

*Please star this project if you find it useful. Thank you.*

## A Lightweight Cross Platform WebSocket Client 

This library is a ground-up implementation of the WebSocket specification [(RFC 6544)](https://tools.ietf.org/html/rfc6455) - i.e., this implementation does not rely on the build-in WebSocket libraries in .NET.

The library allows developers additional flexibility, including the ability to establish secure wss websocket connections to websocket servers that have self-signing certificates, expired certificates etc. This capability should be used with care for obvious reasons, however it is useful for testing environments, closed local networks, local IoT set-ups etc.

Furthermore, this library utilize [ReactiveX](http://reactivex.io/) (aka Rx or Reactive Extensions). Although taking this dependency introduces an added learning curve, it is a learning curve worthwhile investing in, as it IMHO makes using and creating a library like this much more elegant compared to using traditional call-back or events based patterns etc.

## New in version 7.0
At writing time, this library has been around for more than 6 years. The work represented in this repo was mainly initiated on a desire to learn and play around with the technologies involved. 

Unsurprisingly, over the years learning and insights grew and eventually maintaining and looking back at the aging code-base became more and more painful for the ever more trained eye, hence I decided to redo most of it. 

Version 7 is more or less a rewrite of 90+ % of the original code.

The version 7 NuGet package includes both a .NET Standard 2.0 package and a .NET Standard 2.1, with e .NET Standard 2.1 package having a few less dependencies.

### Now With Client Ping

Version 7 introduces a *client ping* feature, which enabling the WebSocket client to send a ping message with a constant interval. 

The `clientPingMessage` parameter is optional and the default value is null. The behavior for the null value is to not include any message, as part of the ping.  

```csharp
var websocketConnectionObservable = 
    client.WebsocketConnectWithStatusObservable(
        uri: WebsocketServerUri, 
        hasClientPing: true, // default is false. 
        clientPingInterval: TimeSpan.FromSeconds(20), // default is 30 seconds.
        clientPingMessage: "my ping message"); // default no message when set to null.
```

It is only possible to use a `string` in this method. For more advanced scenarios, the `ISender` has a `SendPing` method that can be used for full control when sending client pings as `string` or as `byte[]`.

## New in version 6.4

Successfully tested with .NET 6.0.

Previously the library only accepted the `ws` and `wss` scheme. Now `http` and `https` is also supported. 

To further extend supported schemes override the `IsSecureConnectionScheme` method of the `MessageWebSocketRx` class.

The virtual method looks like this:

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
Simplifications and no longer relies on SocketLite but utilizes the cross-platform capabilities of .NET Standard 2.0+.

## New in version 5.0.
From hereon and forward only .NET Standard 2.0+ is supported.

## Usage
For a more detailed sample of using this library please see the [console example app](https://github.com/1iveowl/WebsocketClientLite.PCL/blob/master/src/test/NETCore.Console.Test/Program.cs).

### To instanciate WebSocket lite class
To use the WebSocket client create an instance of the class `MessageWebsocketRx`:

```csharp
var websocketClient = new MessageWebsocketRx()
{
    IgnoreServerCertificateErrors = false,
    Headers = new Dictionary<string, string> {{ "Pragma", "no-cache" }, { "Cache-Control", "no-cache" }}
};
```
... or use the alternative constructor to pass your own TcpClient for more control of the configuration and the management of your TCP socket connection. 
```csharp
MessageWebSocketRx(TcpClient tcpClient)
```

> Note: If the TcpClient is not connected the library will connect it. Also, the TcpClient will not be disposed automatically when passed in using the constructor, as it will in the case when no TcpClient is supplied.

### To connect client to WebSocket server

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

The existing virtual method implementation looks like this:

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

### Working With Slack (And maybe also other WebSocket server implementations)
The [RFC 6455 section defining how ping/pong works](https://tools.ietf.org/html/rfc6455#section-5.5.2) seems to be ambiguous on the question whether or not a pong must include the byte defining the length of data-frame, in the special case when there is no data and the length of the data is zero.

When testing against for instance the Postman WebSocket test server [Postman WebSocket Server](wss://ws.postman-echo.com/raw) the data-frame byte is expected and should have the value 0 (zero), when there's no data in the data-frame. 

However, when used with the [slack.rtm](https://api.slack.com/rtm) API the byte should **not** be there at all in the case of no data in the data-frame, and if it is, the slack WebSocket server will disconnect.

To manage this *length byte-issue* the following property can be set to `true`, in which case the byte with the zero value will NOT be added to the pong. For instance like this:
```csharp
var websocketClient = new MessageWebSocketRx
{
    ExcludeZeroApplicationDataInPong = true
}
```

To further complicate matters the [slack.rtm api](https://api.slack.com/rtm) [seems to requires a ping at the Slack application layer too](http://stackoverflow.com/questions/38171620/slack-rtm-api-disconnection-following-message-in-scala). 

A simplified implementation of this could look like this, which obviously would need to be repeated in some interval to keep the slack connection going:

```csharp
await _webSocket.SendText("{\"id\": 1234, // ID, see \"sending messages\" above\"type\": \"ping\",...}");
```
For details read the **Ping and Pong** section of the [slack.rtm API documentation](https://api.slack.com/rtm) 

### Working with socket.io 
This library have also been tested with [socket.io](https://socket.io/docs/v4/). 

A typical connection will look like this:

```csharp
var websocketConnectionObservable = 
    client.WebsocketConnectWithStatusObservable(
        new Uri($"http://{url}:{port}/socket.io/?EIO=4&transport=websocket"));
```

This will connect on the WebSocket layer with socket.io server. 

To further connect on socket.io level see documentation. For instance, typically a text message with the content `40` need to be send right after the connection have been established. Also, some socket.io server implementations seem to be very sensitive to the encoding of the messages that are being send, and will disconnect immediately if receiving a data-frame with a text message that does not comply with the expected socket.io encoding protocol.

For more see here: [WebSocket client not connecting to the socket.io server](https://github.com/socketio/socket.io/discussions/4299).


### References:
The following documentation was utilized when writing this library:

 - [RFC 6544](https://tools.ietf.org/html/rfc6455)
 - [Writing WebSocket Servers](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_servers)
 - [Writing WebSocket Server in C#](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server)
 - [Writing WebSocket client applications](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_client_applications)

### Thank you !

Thank you to all the developers who've been using this library through the years, many of which that have reported issues or bugs, or made contributions and pull requests to make the library better and/or more capable. It is this interaction with all of you that makes sharing and learning fun.

