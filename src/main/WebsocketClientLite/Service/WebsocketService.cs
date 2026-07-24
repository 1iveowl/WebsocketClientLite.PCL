using System;
using System.Threading.Tasks;

namespace WebsocketClientLite.Service;

internal sealed class WebsocketService : IDisposable, IAsyncDisposable
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

    /// <summary>
    /// Graceful teardown: awaits the close handshake via the connection handler
    /// before the remaining resources are released.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Dispose in reverse dependency order
        await WebsocketConnectionHandler.DisposeAsync().ConfigureAwait(false);
        _websocketParserHandler?.Dispose();
        _tcpConnectionService?.Dispose();
    }

    public void Dispose()
    {
        // Dispose in reverse dependency order
        WebsocketConnectionHandler?.Dispose();
        _websocketParserHandler?.Dispose();
        _tcpConnectionService?.Dispose();
    }
}
