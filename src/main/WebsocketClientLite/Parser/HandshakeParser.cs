using HttpMachine;
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using WebsocketClientLite.CustomException;
using IWebsocketClientLite;

namespace WebsocketClientLite.Parser;

internal class HandshakeParser(
    HttpCombinedParser parserHandler,
    HandshakeParserDelegate parserDelegate,
    Action<ConnectionStatus, Exception> connectionStatusAction)
{
    private readonly Action<ConnectionStatus, Exception> _connectionStatusAction = connectionStatusAction;
    private readonly HttpCombinedParser _parserHandler = parserHandler;
    private readonly HandshakeParserDelegate _parserDelegate = parserDelegate;

    internal IEnumerable<string>? SubprotocolAcceptedNames { get; private set; }

    internal bool Parse(
        ReadOnlySpan<byte> bytes,
        IEnumerable<string>? subProtocols)
    {
        _parserHandler.Execute(bytes);

        if (_parserDelegate.HttpRequestResponse is not null
            && _parserDelegate.HttpRequestResponse.IsEndOfMessage)
        {
            if (_parserDelegate.HttpRequestResponse.StatusCode == 101)
            {
                if (subProtocols is not null 
                    && _parserDelegate?.HttpRequestResponse?.Headers is not null)
                {
                    // Natural casing: header lookups are case-insensitive as of
                    // HttpMachine 6.0, so no dependency on the parser's header
                    // name normalization remains.
                    if (_parserDelegate
                        .HttpRequestResponse
                        .Headers
                        .TryGetValue("Sec-WebSocket-Protocol", out var subprotocolAcceptedNames))
                    {
                        // Materialize: evaluated once, and consumers (the public
                        // NegotiatedSubprotocols property) get a stable snapshot.
                        SubprotocolAcceptedNames = subprotocolAcceptedNames
                            .Where(spn => subProtocols.Contains(spn))
                            .ToList();

                        if (!SubprotocolAcceptedNames?.Any() ?? true)
                        {
                            _connectionStatusAction(
                                ConnectionStatus.Aborted,
                                new WebsocketClientLiteException("Server responded only with subprotocols not known by client."));
                        }
                    }
                    else
                    {
                        _connectionStatusAction(
                            ConnectionStatus.Aborted,
                            new WebsocketClientLiteException("Server responded with blank Sub Protocol name")
                            );
                    }
                }

                Debug.WriteLine("HandShake completed");
                return true;
            }
            else
            {
                // Non-101 response: the HTTP message is complete, so parsing is
                // done. The failure itself is reported by HandshakeParserDelegate
                // (HandshakeFailed + exception), which the connect path awaits —
                // no side-channel Abort needed, and no further bytes to read.
                Debug.WriteLine($"Handshake rejected: {_parserDelegate.HttpRequestResponse.StatusCode}");
                return true;
            }
        }

        return false;
    }
}
