using HttpMachine;
using IHttpMachine;
using System;
using System.Collections.Generic;
using WebsocketClientLite.CustomException;
using WebsocketClientLite.Model;

namespace WebsocketClientLite.Parser;

internal class HandshakeParserDelegate(
    IObserver<(HandshakeStateKind handshakeState, WebsocketClientLiteException? ex)> observerHandshakeParserState) : HttpParserDelegate
{
    internal Dictionary<string, List<string>> HeaderValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal int StatusCode { get; private set; }
    private string? _header;

    public override void OnHeaderName(IHttpCombinedParser parser, string name)
    {
        base.OnHeaderName(parser, name);
        _header = name;
        if (!HeaderValues.ContainsKey(name)) HeaderValues.Add(name, new List<string>());
    }

    public override void OnHeaderValue(IHttpCombinedParser parser, string value)
    {
        base.OnHeaderValue(parser, value);
        HeaderValues[_header!].Add(value);
    }

    public override void OnResponseCode(IHttpCombinedParser parser, int statusCode, string reason)
    {
        base.OnResponseCode(parser, statusCode, reason);
        StatusCode = statusCode;
    }

    public override void OnParserError() => throw new WebsocketClientLiteException("Malformed HTTP handshake response.");

    internal bool HeadersComplete { get; private set; }

    public override void OnHeadersEnd(IHttpCombinedParser parser)
    {
        base.OnHeadersEnd(parser);
        HeadersComplete = true;
    }

    // HTTP message completion is not WebSocket acceptance. The parser reports
    // the outcome only after checking the response against the sent request.
    internal void Report(bool accepted, string reason) => observerHandshakeParserState.OnNext(
        accepted ? (HandshakeStateKind.HandshakeCompletedSuccessfully, null)
        : (HandshakeStateKind.HandshakeFailed, new WebsocketClientLiteException(reason)));
}
