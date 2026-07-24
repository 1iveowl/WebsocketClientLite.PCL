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
    private static (
        HandshakeParser parser,
        List<ConnectionStatus> statuses,
        List<(HandshakeStateKind state, WebsocketClientLiteException? ex)> states) Create()
    {
        var statuses = new List<ConnectionStatus>();
        var states = new List<(HandshakeStateKind state, WebsocketClientLiteException? ex)>();
        var observer = Observer.Create<(HandshakeStateKind state, WebsocketClientLiteException? ex)>(
            states.Add, _ => { }, () => { });
        var parserDelegate = new HandshakeParserDelegate(observer);
        var parserHandler = new HttpCombinedParser(parserDelegate);
        var parser = new HandshakeParser(parserHandler, parserDelegate, (status, _) => statuses.Add(status));
        return (parser, statuses, states);
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
    public void Parse_SwitchingProtocols_ReportsSuccess()
    {
        var (parser, statuses, states) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo="), null);

        Assert.True(done);
        Assert.DoesNotContain(ConnectionStatus.Aborted, statuses);
        Assert.Contains(states, s => s.state == HandshakeStateKind.HandshakeCompletedSuccessfully);
    }

    [Fact]
    public void Parse_NonSwitchingProtocols_CompletesWithHandshakeFailed()
    {
        var (parser, _, states) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 404 Not Found",
            "Content-Length: 0"), null);

        // The HTTP response is complete, so parsing is done — and the outcome is
        // a failed handshake carrying the server's status code, not success.
        Assert.True(done);
        var failed = Assert.Single(states);
        Assert.Equal(HandshakeStateKind.HandshakeFailed, failed.state);
        Assert.NotNull(failed.ex);
        Assert.Contains("404", failed.ex!.Message);
    }

    [Fact]
    public void Parse_AcceptedSubprotocol_DoesNotAbort()
    {
        var (parser, statuses, states) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "Sec-WebSocket-Protocol: chat"), new[] { "chat" });

        Assert.True(done);
        Assert.DoesNotContain(ConnectionStatus.Aborted, statuses);
        Assert.Contains(states, s => s.state == HandshakeStateKind.HandshakeCompletedSuccessfully);
    }

    [Fact]
    public void Parse_101WithoutFramingHeaders_CompletesAtHeaderTerminator()
    {
        // A 101 response carries neither Content-Length nor Transfer-Encoding.
        // The handshake read loop depends on the parser reporting end-of-message
        // right at the blank line — never waiting for EOF. HttpMachine 6.x
        // deliberately defers the RFC 9112 §6.3 rule-8 change (close-delimited
        // responses); if a future HttpMachine applies it, this test fails loudly
        // instead of WaitForHandshake hanging forever.
        var (parser, _, states) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade"), null);

        Assert.True(done);
        Assert.Contains(states, s => s.state == HandshakeStateKind.HandshakeCompletedSuccessfully);
    }

    [Fact]
    public void Parse_OneByteAtATime_CompletesExactlyAtTerminator()
    {
        // Mirrors the actual read loop, which feeds the parser one-byte spans:
        // the parser must report completion on exactly the final byte of the
        // header terminator and never before.
        var (parser, _, _) = Create();
        var bytes = Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket");

        for (int i = 0; i < bytes.Length; i++)
        {
            var done = parser.Parse(bytes.AsSpan(i, 1), null);
            Assert.Equal(i == bytes.Length - 1, done);
        }
    }

    [Fact]
    public void Parse_SubprotocolHeader_IsCaseInsensitive()
    {
        // HttpMachine 6.0 made header lookups case-insensitive; the subprotocol
        // negotiation must work whatever casing the server chose.
        var (parser, statuses, states) = Create();

        var done = parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "sec-websocket-protocol: chat"), new[] { "chat" });

        Assert.True(done);
        Assert.DoesNotContain(ConnectionStatus.Aborted, statuses);
        Assert.Contains(states, s => s.state == HandshakeStateKind.HandshakeCompletedSuccessfully);
    }

    [Fact]
    public void Parse_UnknownSubprotocol_AbortsConnection()
    {
        var (parser, statuses, _) = Create();

        parser.Parse(Response(
            "HTTP/1.1 101 Switching Protocols",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "Sec-WebSocket-Protocol: superchat"), new[] { "chat" });

        // Server selected a subprotocol the client never offered.
        Assert.Contains(ConnectionStatus.Aborted, statuses);
    }
}
