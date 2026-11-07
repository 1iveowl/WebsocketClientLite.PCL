using HttpMachine;
using System;
using System.Linq;
using System.Collections.Generic;
using WebsocketClientLite.CustomException;
using IWebsocketClientLite;
using Protocol = WebsocketClientLite.VerifiedProtocol.Protocol;

namespace WebsocketClientLite.Parser;

internal class HandshakeParser(HttpCombinedParser parserHandler, HandshakeParserDelegate parserDelegate,
    Action<ConnectionStatus, Exception> connectionStatusAction, string expectedAccept)
{
    internal IEnumerable<string>? SubprotocolAcceptedNames { get; private set; }
    private bool _done;

    internal bool Parse(ReadOnlySpan<byte> bytes, IEnumerable<string>? subProtocols)
    {
        if (_done) return true;
        parserHandler.Execute(bytes);
        if (!parserDelegate.HeadersComplete) return false;
        _done = true;

        string[] Values(string name) => parserDelegate.HeaderValues.TryGetValue(name, out var values)
            ? values.ToArray() : Array.Empty<string>();
        bool Token(string name, string token) => Values(name).SelectMany(v => v.Split(','))
            .Any(v => string.Equals(v.Trim(), token, StringComparison.OrdinalIgnoreCase));

        var accept = Values("Sec-WebSocket-Accept");
        var selected = Values("Sec-WebSocket-Protocol");
        bool protocolValid = selected.Length == 0 || (selected.Length == 1 &&
            !selected[0].Any(c => c == ',') && (subProtocols?.Contains(selected[0].Trim(), StringComparer.Ordinal) ?? false));
        bool accepted = Protocol.HandshakeValid(parserDelegate.StatusCode == 101 && parserHandler.MajorVersion == 1 && parserHandler.MinorVersion == 1,
            Token("Upgrade", "websocket"), Token("Connection", "Upgrade"),
            accept.Length == 1 && string.Equals(accept[0].Trim(), expectedAccept, StringComparison.Ordinal),
            protocolValid, Values("Sec-WebSocket-Extensions").Length == 0);
        if (accepted && selected.Length == 1) SubprotocolAcceptedNames = new[] { selected[0].Trim() };
        string reason = $"Invalid WebSocket handshake response (HTTP {parserDelegate.StatusCode}): required upgrade, accept, or negotiation checks failed.";
        // Report failure through the handshake result; retain the existing status notification.
        parserDelegate.Report(accepted, reason);
        if (!accepted) connectionStatusAction(ConnectionStatus.Aborted, new WebsocketClientLiteException(reason));
        return true;
    }
}
