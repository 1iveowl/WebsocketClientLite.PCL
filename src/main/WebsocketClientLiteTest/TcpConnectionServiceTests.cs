using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using WebsocketClientLite.Service;
using Xunit;

namespace WebsocketClientLiteTest;

public class TcpConnectionServiceTests
{
    [Fact]
    public void ConnectionStream_BeforeConnect_ThrowsInvalidOperation()
    {
        using var service = new TcpConnectionService(
            () => false,
            (o, c, ch, e) => true,
            (client, uri) => Task.CompletedTask,
            (status, ex) => { },
            true,
            new TcpClient());

        // Invalid state (not connected yet) must surface as InvalidOperationException,
        // not ArgumentNullException with the message in the paramName slot.
        Assert.Throws<InvalidOperationException>(() => service.ConnectionStream);
    }
}
