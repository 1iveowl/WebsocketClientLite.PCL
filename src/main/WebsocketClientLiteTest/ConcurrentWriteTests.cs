using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.Service;
using Xunit;

namespace WebsocketClientLiteTest;

public class ConcurrentWriteTests
{
    // A write-only stream that flags if more than one WriteAsync is ever in
    // flight at the same time. A small delay widens the race window.
    private sealed class ConcurrencyDetectingStream : Stream
    {
        private int _active;
        public bool ConcurrencyDetected { get; private set; }

        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _active) > 1)
            {
                ConcurrencyDetected = true;
            }

            try
            {
                await Task.Delay(2, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class FakeConnection : TcpConnectionService
    {
        public FakeConnection(Stream stream) : base(
            () => false,
            (o, c, ch, e) => true,
            (client, uri) => Task.CompletedTask,
            (status, ex) => { },
            true,
            new TcpClient())
        {
            typeof(TcpConnectionService)
                .GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(this, stream);
        }
    }

    [Fact]
    public async Task ConcurrentSends_AreSerialized()
    {
        var stream = new ConcurrencyDetectingStream();
        var connection = new FakeConnection(stream);

        // Mirrors the factory's WriteToStream (writes `count` bytes, then flushes).
        Func<Stream, byte[], int, CancellationToken, Task> writeFunc = async (s, bytes, count, ct) =>
        {
            await s.WriteAsync(bytes.AsMemory(0, count), ct);
            await s.FlushAsync(ct);
        };

        var sender = new WebsocketSenderHandler(connection, (status, ex) => { }, writeFunc, false);

        // Fire many writes concurrently from different "sources" (text + pings),
        // mimicking user sends racing with the client-ping timer.
        var tasks = Enumerable.Range(0, 50)
            .Select(i => i % 2 == 0
                ? sender.SendText($"message {i}")
                : sender.SendPing("ping"))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.False(stream.ConcurrencyDetected, "Writes to the connection stream were not serialized.");
    }
}
