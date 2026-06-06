using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite;
using WebsocketClientLite.Service;
using Xunit;

namespace WebsocketClientLiteTest;

public class SendFrameTests
{
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

    // Builds a sender whose writes are captured verbatim (exactly `count` bytes,
    // so the framing/length math is validated too).
    private static WebsocketSenderHandler CreateSender(List<byte[]> captured)
    {
        var connection = new FakeConnection(new MemoryStream());

        Func<Stream, byte[], int, CancellationToken, Task> writeFunc = (s, bytes, count, ct) =>
        {
            captured.Add(bytes.AsSpan(0, count).ToArray());
            return Task.CompletedTask;
        };

        return new WebsocketSenderHandler(connection, (status, ex) => { }, writeFunc, false);
    }

    private static byte[] Unmask(byte[] payload, byte[] key) =>
        payload.Select((b, i) => (byte)(b ^ key[i % 4])).ToArray();

    [Fact]
    public async Task SendText_ProducesMaskedSingleTextFrame()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);

        await sender.SendText("hello");

        var frame = Assert.Single(frames);
        Assert.Equal(0x81, frame[0]);       // FIN + Text opcode
        Assert.Equal(0x80 | 5, frame[1]);   // mask bit + 7-bit length 5
        var key = frame[2..6];
        Assert.Equal("hello", Encoding.UTF8.GetString(Unmask(frame[6..], key)));
    }

    [Fact]
    public async Task SendText_16BitLength_EncodesLengthBigEndian()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);
        var text = new string('x', 300);

        await sender.SendText(text);

        var frame = Assert.Single(frames);
        Assert.Equal(0x81, frame[0]);
        Assert.Equal(0x80 | 126, frame[1]);             // mask bit + 16-bit length marker
        Assert.Equal(300, (frame[2] << 8) | frame[3]);  // big-endian extended length
        var key = frame[4..8];
        Assert.Equal(text, Encoding.UTF8.GetString(Unmask(frame[8..], key)));
    }

    [Fact]
    public async Task SendText_MultibyteUtf8_SizesFrameByByteCount()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);
        var text = "héllo·wörld·☃";          // multibyte: byte count > char count
        var byteLen = Encoding.UTF8.GetByteCount(text);

        await sender.SendText(text);

        var frame = Assert.Single(frames);
        Assert.Equal(0x81, frame[0]);
        Assert.Equal(0x80 | byteLen, frame[1]);   // length field uses byte count, not char count
        var key = frame[2..6];
        Assert.Equal(text, Encoding.UTF8.GetString(Unmask(frame[6..], key)));
    }

    [Fact]
    public async Task SendBinary_ProducesMaskedBinaryFrame()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

        await sender.SendBinary(data, CancellationToken.None);

        var frame = Assert.Single(frames);
        Assert.Equal(0x82, frame[0]);       // FIN + Binary opcode
        Assert.Equal(0x80 | 7, frame[1]);
        var key = frame[2..6];
        Assert.Equal(data, Unmask(frame[6..], key));
    }

    [Fact]
    public async Task SendText_ManualFragments_SetFinAndOpcodeBits()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);

        await sender.SendText("a", OpcodeKind.Text, FragmentKind.First);
        await sender.SendText("b", OpcodeKind.Continuation, FragmentKind.None);
        await sender.SendText("c", OpcodeKind.Text, FragmentKind.Last);

        Assert.Equal(0x01, frames[0][0]); // First: FIN=0, opcode=Text
        Assert.Equal(0x00, frames[1][0]); // Middle: FIN=0, opcode=Continuation
        Assert.Equal(0x80, frames[2][0]); // Last: FIN=1, opcode=Continuation
    }
}
