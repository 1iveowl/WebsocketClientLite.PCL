using System;
using System.Collections.Generic;
using System.Text;
using WebsocketClientLite.Helper;
using Xunit;

namespace WebsocketClientLiteTest;

public class HandshakeComposeTests
{
    private static readonly Uri _uri = new("wss://example.com/ws");

    [Fact]
    public void Compose_ValidInputs_ProducesHandshake()
    {
        var bytes = ClientHandShake.Compose(
            _uri,
            origin: "https://example.com",
            headers: new Dictionary<string, string> { { "Authorization", "Bearer token" } },
            subprotocols: new[] { "chat" });

        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("GET /ws HTTP/1.1\r\n", text);
        Assert.Contains("Authorization: Bearer token\r\n", text);
        Assert.Contains("Origin: https://example.com\r\n", text);
        Assert.Contains("Sec-WebSocket-Protocol: chat\r\n", text);
        Assert.EndsWith("\r\n\r\n", text);
    }

    [Fact]
    public void Compose_HeaderValueWithCrlf_Throws()
    {
        var headers = new Dictionary<string, string>
        {
            { "X-Test", "value\r\nInjected: evil" }
        };

        Assert.Throws<ArgumentException>(() => ClientHandShake.Compose(_uri, headers: headers));
    }

    [Fact]
    public void Compose_HeaderNameWithCrlf_Throws()
    {
        var headers = new Dictionary<string, string>
        {
            { "X-Test\r\nInjected", "value" }
        };

        Assert.Throws<ArgumentException>(() => ClientHandShake.Compose(_uri, headers: headers));
    }

    [Fact]
    public void Compose_OriginWithCrlf_Throws() =>
        Assert.Throws<ArgumentException>(
            () => ClientHandShake.Compose(_uri, origin: "https://evil\r\nInjected: x"));

    [Fact]
    public void Compose_SubprotocolWithCrlf_Throws() =>
        Assert.Throws<ArgumentException>(
            () => ClientHandShake.Compose(_uri, subprotocols: new[] { "chat\r\nInjected: x" }));
}
