using System;

namespace WebsocketClientLite.Service;

internal sealed class WebsocketService : IDisposable
{
    private readonly TcpConnectionService _tcpConnectionService;
    private readonly WebsocketParserHandler _websocketParserHandler;
    internal WebsocketConnectionHandler WebsocketConnectionHandler { get; }

    internal WebsocketService(
        TcpConnectionService tcpConnectionService,
        WebsocketParserHandler websocketParserHandler,
        WebsocketConnectionHandler websocketConnectionHandler)
    {
        _tcpConnectionService = tcpConnectionService;
        _websocketParserHandler = websocketParserHandler;
        WebsocketConnectionHandler = websocketConnectionHandler;
    }

    public void Dispose()
    {
        // Dispose in reverse dependency order
        WebsocketConnectionHandler?.Dispose();
        _websocketParserHandler?.Dispose();
        _tcpConnectionService?.Dispose();
    }
}
