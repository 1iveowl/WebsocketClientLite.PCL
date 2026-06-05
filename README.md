# WebSocket Client Lite (Rx)

[![NuGet Badge](https://img.shields.io/nuget/v/WebsocketClientLite.PCL)](https://www.nuget.org/packages/WebsocketClientLite.PCL)

[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.0-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library)[![.NET Standard](http://img.shields.io/badge/.NET_Standard-v2.1-red.svg)](https://docs.microsoft.com/da-dk/dotnet/articles/standard/library)

[![.NET 8](http://img.shields.io/badge/.NET-v8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)[![.NET 9](http://img.shields.io/badge/.NET-v9.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![.NET 10](http://img.shields.io/badge/.NET-v10.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

[![System.Reactive](http://img.shields.io/badge/Rx-v6.1.0-ff69b4.svg)](http://reactivex.io/)

![CI/CD](https://github.com/1iveowl/WebsocketClientLite.PCL/actions/workflows/build.yml/badge.svg)

*Please star this project if you find it useful. Thank you.*

## TL;DR

See the project `NETCore.Console.Test` in the `./src/test` directory for an example of how to use.

## A Lightweight Cross Platform WebSocket Client

This library is a ground-up implementation of the WebSocket specification [(RFC 6455)](https://tools.ietf.org/html/rfc6455) - it does not rely on any built-in .NET WebSocket libraries.

The library provides developers with additional flexibility, including the ability to establish secure WSS websocket connections to servers with self-signing certificates, expired certificates, etc. This capability should be used with care for obvious reasons, but is valuable for testing environments, closed local networks, local IoT set-ups, and more.

The library utilizes [ReactiveX](http://reactivex.io/) (aka Rx or Reactive Extensions). While this dependency introduces a small learning curve, it's worthwhile for the context.

## New in Version 9.0

Version 9.0 is a correctness and protocol-compliance release.

**Bug fixes**

- **Subprotocols are now sent.** The `Subprotocols` you configure are now included in the `Sec-WebSocket-Protocol` handshake header. Previously they were only used to validate the server's response and never actually sent.
- **Close codes use network byte order.** WebSocket close status codes are now sent big-endian as required by [RFC 6455 §5.5.1](https://tools.ietf.org/html/rfc6455#section-5.5.1); they were previously byte-swapped on little-endian platforms.
- **`ExcludeZeroApplicationDataInPong` now works.** The flag is honored when replying to a zero-length ping (required for Slack's RTM API). Previously it had no effect.
- **`CancellationToken` is honored.** The token passed to `WebsocketConnectObservable` is now forwarded to the underlying connection. Previously it was ignored on that overload.
- **More robust teardown.** Client-ping failures no longer surface as an unhandled exception on a background thread, a disposed read stream no longer yields a bogus empty frame, a connection closed mid-handshake now reports a clear error, and per-connection resources are disposed correctly.

**Security hardening**

- **Incoming frame/message size cap.** A new `MaxFrameSize` property (default 16 MiB) bounds both a single incoming frame and a fully reassembled fragmented message, preventing a malicious server from exhausting memory. See [Limiting incoming message size](#limiting-incoming-message-size).
- **Control-frame validation.** Control frames (Close/Ping/Pong) that are fragmented or exceed 125 bytes are now rejected per [RFC 6455 §5.5](https://tools.ietf.org/html/rfc6455#section-5.5).
- **Handshake header-injection guard.** Headers, `Origin`, and subprotocol values containing CR, LF, or NUL are now rejected, preventing HTTP header injection / request smuggling.
- **Certificate revocation checking.** A new `CheckCertificateRevocation` property (default `true`) enables TLS revocation checking. See [Certificate revocation](#certificate-revocation).

**Breaking changes**

- `CheckCertificateRevocation` now defaults to `true` (TLS revocation checking is performed). If your environment cannot reach the certificate's OCSP/CRL endpoints, set it to `false`.

- `ClientWebSocketRx` is now a `class` instead of a `record`. A connection holds live, disposable state, so value-equality and `with` semantics were both misleading and incorrect.
- `ClientWebSocketRx.TcpClient` is now nullable (`TcpClient?`). Leave it unset to have the client create one with the address family matching the target URI and dispose it automatically, or supply your own and control its lifetime via `HasTransferSocketLifeCycleOwnership`.

**Internal cleanup**

- Removed dead code (unused masking helpers, an unused scheduler, a duplicate property) and an extra per-frame buffer allocation on the send path.

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

The library supports the `ws`, `wss`, `http`, and `https` URI schemes. You can extend the supported schemes by overriding the `IsSecureConnectionScheme` method of the `ClientWebSocketRx` class. The default implementation looks like this:

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
using System.Reactive.Disposables;
using System.Reactive.Linq;

using CompositeDisposable disposables = new();

Func<IObservable<(IDataframe dataframe, ConnectionStatus state)>> connect = () =>
    client.WebsocketConnectWithStatusObservable(
        uri: new Uri("wss://ws.postman-echo.com/raw"),
        hasClientPing: true,
        clientPingInterval: TimeSpan.FromSeconds(10),
        clientPingMessage: "ping message",
        cancellationToken: cts.Token);

IDisposable connectionSubscription = Observable.Defer(connect)
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

        Console.WriteLine(tuple.state);

        if (tuple.state == ConnectionStatus.DataframeReceived && tuple.dataframe is not null)
        {
            Console.WriteLine($"Received: {tuple.dataframe.Message}");
        }
    })
    .Where(t => t.state == ConnectionStatus.WebsocketConnected)
    // SendMessages() is your own method that uses client.Sender once connected.
    .SelectMany(_ => Observable.FromAsync(_ => SendMessages()))
    .Subscribe();

disposables.Add(connectionSubscription);
```

### Handling Connection Status and Messages

The observable pipeline above logs each connection state and prints received messages. It automatically retries and repeats the connection on errors and ensures resources are cleaned up via the `CompositeDisposable`.
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
// Option 1: Ignore all certificate errors.
// ⚠️ WARNING: this disables ALL certificate validation and exposes the
// connection to man-in-the-middle attacks. Use it only for local testing
// against self-signed/expired certificates. NEVER enable it in production.
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

#### Certificate revocation

Revocation checking is performed during the TLS handshake by default
(`CheckCertificateRevocation = true`). If your environment cannot reach the
certificate's OCSP/CRL endpoints (e.g. some corporate proxies or air-gapped
networks), the handshake will fail; disable the check in that case:

```csharp
var client = new ClientWebSocketRx { CheckCertificateRevocation = false };
```

This setting has no effect when `IgnoreServerCertificateErrors` is `true`.

### Limiting incoming message size

By default the client rejects any incoming frame — or any reassembled
fragmented message — larger than 16 MiB, then tears down the connection. This
guards against a malicious or misbehaving server exhausting memory by declaring
a huge payload length or flooding fragments. Adjust or disable the limit with
`MaxFrameSize`:

```csharp
// Cap incoming frames/messages at 4 MiB.
var client = new ClientWebSocketRx { MaxFrameSize = 4 * 1024 * 1024 };

// Or allow up to int.MaxValue bytes (no explicit limit — not recommended
// when connecting to untrusted servers).
var unlimited = new ClientWebSocketRx { MaxFrameSize = 0 };
```

### Working with Specialized WebSocket Implementations

#### Slack RTM API

The [RFC 6455 section defining how ping/pong works](https://tools.ietf.org/html/rfc6455#section-5.5.2) seems to be ambiguous on the question whether or not a pong must include the byte defining the length of data-frame, in the special case when there is no data and the length of the data is zero.

When testing against for instance the Postman WebSocket test server [Postman WebSocket Server](wss://ws.postman-echo.com/raw) the data-frame byte is expected and should have the value 0 (zero), when there's no data in the data-frame. 

However, when used with the [slack.rtm](https://api.slack.com/rtm) API the byte should **not** be there at all in the case of no data in the data-frame, and if it is, the slack WebSocket server will disconnect.

To manage this *length-byte issue*, set the following property to `true`, in which case the zero-value length byte will not be added to the pong. This is required for Slack's RTM API and similar services:

```csharp
// Required for Slack's RTM API
var client = new ClientWebSocketRx { ExcludeZeroApplicationDataInPong = true };
```

Slack RTM also requires application-level ping messages:

```csharp
// Slack expects an application-level ping as a JSON text message.
// The id is your own correlation id; see "Sending Messages" above.
await sender.SendText("{\"id\": 1234, \"type\": \"ping\"}");
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

This library targets .NET Standard 2.0, .NET Standard 2.1, .NET 8, .NET 9, and .NET 10. The CI/CD pipeline uses GitHub Actions to build, test, and publish packages.

To build locally you need the matching .NET SDKs installed. The .NET 10 SDK can build every target framework; with an older SDK only the target frameworks it supports will build.

For contributors and developers, please ensure your changes maintain compatibility with these target frameworks.

## Thank You

Thank you to all the developers who have used this library over the years, reported issues, submitted bug fixes, or made contributions to improve the library. Your feedback and support make open source development rewarding and educational.
