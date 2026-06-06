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
using WebsocketClientLite.Model;
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
    private static WebsocketSenderHandler CreateSender(
        List<byte[]> captured,
        bool excludeZeroApplicationDataInPong = false)
    {
        var connection = new FakeConnection(new MemoryStream());

        Func<Stream, byte[], int, CancellationToken, Task> writeFunc = (s, bytes, count, ct) =>
        {
            captured.Add(bytes.AsSpan(0, count).ToArray());
            return Task.CompletedTask;
        };

        return new WebsocketSenderHandler(connection, (status, ex) => { }, writeFunc, excludeZeroApplicationDataInPong);
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
    public async Task SendText_List_SendsFirstContinuationLastSequence()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);

        await sender.SendText(new[] { "a", "b", "c" });

        Assert.Equal(3, frames.Count);
        Assert.Equal(0x01, frames[0][0]); // First: FIN=0, opcode=Text
        Assert.Equal(0x00, frames[1][0]); // Middle: FIN=0, opcode=Continuation
        Assert.Equal(0x80, frames[2][0]); // Last: FIN=1, opcode=Continuation
        Assert.Equal("a", Encoding.UTF8.GetString(Unmask(frames[0][6..], frames[0][2..6])));
        Assert.Equal("c", Encoding.UTF8.GetString(Unmask(frames[2][6..], frames[2][2..6])));
    }

    [Fact]
    public async Task SendText_SingleItemList_SendsOneCompleteFrame()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);

        await sender.SendText(new[] { "solo" });

        var frame = Assert.Single(frames);
        Assert.Equal(0x81, frame[0]); // single complete Text frame (FIN + Text)
        Assert.Equal("solo", Encoding.UTF8.GetString(Unmask(frame[6..], frame[2..6])));
    }

    [Fact]
    public async Task SendPing_ProducesPingFrame()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);

        await sender.SendPing("hi");

        var frame = Assert.Single(frames);
        Assert.Equal(0x89, frame[0]); // FIN + Ping
        Assert.Equal("hi", Encoding.UTF8.GetString(Unmask(frame[6..], frame[2..6])));
    }

    [Fact]
    public async Task SendCloseHandshake_UsesBigEndianStatusCode()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);

        await sender.SendCloseHandshakeAsync(StatusCodes.GoingAway); // 1001

        var frame = Assert.Single(frames);
        Assert.Equal(0x88, frame[0]); // FIN + Close
        var payload = Unmask(frame[6..], frame[2..6]);
        Assert.Equal(0x03, payload[0]); // 1001 >> 8  (network byte order)
        Assert.Equal(0xE9, payload[1]); // 1001 & 0xFF
    }

    [Fact]
    public async Task SendPong_ExcludeZeroApplicationData_SendsSingleOpcodeByte()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames, excludeZeroApplicationDataInPong: true);

        await sender.SendPong(new Dataframe { Payload = Array.Empty<byte>() }, CancellationToken.None);

        var frame = Assert.Single(frames);
        Assert.Equal(new byte[] { 0x8A }, frame); // FIN + Pong only, no length byte (Slack RTM)
    }

    [Fact]
    public async Task SendPong_WithData_SendsMaskedPongFrame()
    {
        var frames = new List<byte[]>();
        var sender = CreateSender(frames);
        var data = new byte[] { 9, 8, 7 };

        await sender.SendPong(new Dataframe { Payload = data }, CancellationToken.None);

        var frame = Assert.Single(frames);
        Assert.Equal(0x8A, frame[0]); // FIN + Pong
        Assert.Equal(data, Unmask(frame[6..], frame[2..6]));
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
