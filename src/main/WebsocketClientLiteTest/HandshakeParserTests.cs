using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;
using HttpMachine;
using IWebsocketClientLite;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;
using WebsocketClientLite.Parser;
using Xunit;

namespace WebsocketClientLiteTest;

public class HandshakeParserTests
{
    private static (HandshakeParser parser, List<ConnectionStatus> statuses) Create()
    {
        var statuses = new List<ConnectionStatus>();
        var observer = Observer.Create<(HandshakeStateKind state, WebsocketClientLiteException? ex)>(
            _ => { }, _ => { }, () => { });
        var parserDelegate = new HandshakeParserDelegate(observer);
        var parserHandler = new HttpCombinedParser(parserDelegate);
        var parser = new HandshakeParser(parserHandler, parserDelegate, (status, _) => statuses.Add(status));
        return (parser, statuses);
    }

    private static byte[] Response(params string[] headerLines)
    {
        var sb = new StringBuilder();
        foreach (var line in headerLines)
        {
            sb.Append(line).Append("\r\n");
        }
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void Parse_SwitchingProtocols_ReturnsTrueWithoutAbort()
    {
        var (parser, statuses) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo="), null);

        Assert.True(done);
        Assert.DoesNotContain(ConnectionStatus.Aborted, statuses);
    }

    [Fact]
    public void Parse_NonSwitchingProtocols_AbortsConnection()
    {
        var (parser, statuses) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 404 Not Found",
            "Content-Length: 0"), null);

        Assert.False(done);
        Assert.Contains(ConnectionStatus.Aborted, statuses);
    }

    [Fact]
    public void Parse_AcceptedSubprotocol_DoesNotAbort()
    {
        var (parser, statuses) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "Sec-WebSocket-Protocol: chat"), new[] { "chat" });

        Assert.True(done);
        Assert.DoesNotContain(ConnectionStatus.Aborted, statuses);
    }

    [Fact]
    public void Parse_UnknownSubprotocol_AbortsConnection()
    {
        var (parser, statuses) = Create();

        parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "Sec-WebSocket-Protocol: superchat"), new[] { "chat" });

        // Server selected a subprotocol the client never offered.
        Assert.Contains(ConnectionStatus.Aborted, statuses);
    }
}
