using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.Model;
using WebsocketClientLite.Service;
using WebsocketClientLite.Helper;
using Xunit;
using System.Reflection;

namespace WebsocketClientLiteTest;

public class PayloadParsingTests
{
    private class FakeTcpConnectionService : TcpConnectionService
    {
        public FakeTcpConnectionService(byte[] bytes) : base(
            () => false,
            (o, c, ch, e) => true,
            (client, uri) => Task.CompletedTask,
            (status, ex) => { },
            true,
            new System.Net.Sockets.TcpClient())
        {
            var streamField = typeof(TcpConnectionService)
                .GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic);
            streamField!.SetValue(this, new MemoryStream(bytes));
        }
    }

    private async Task<Dataframe?> Parse(byte[] frame)
    {
        var fake = new FakeTcpConnectionService(frame);
        return await fake.CreateDataframe(CancellationToken.None)
            .PayloadBitLenght()
            .PayloadLenght()
            .GetPayload(CancellationToken.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(125)]
    public async Task Parse_8Bit_Length(byte length)
    {
        byte opcodeFin = 0x81;
        byte maskLen = (byte)(0x80 | length);
        byte[] mask = { 1, 2, 3, 4 };
        byte[] payload = Enumerable.Repeat((byte)'a', length).ToArray();
        byte[] encoded = new byte[length];
        for (int i = 0; i < length; i++) encoded[i] = (byte)(payload[i] ^ mask[i % 4]);

        var frame = new byte[2 + 4 + length];
        frame[0] = opcodeFin; frame[1] = maskLen;
        Array.Copy(mask, 0, frame, 2, 4);
        Array.Copy(encoded, 0, frame, 6, length);

        var df = await Parse(frame);
        Assert.NotNull(df);
        Assert.Equal((ulong)length, df!.Length);
        Assert.Equal(new string('a', length), df.Message);
    }

    [Fact]
    public async Task Parse_16Bit_Length()
    {
        ushort length = 300;
        byte opcodeFin = 0x81;
        byte firstLen = 0xFE; // 126 + mask bit
        byte[] mask = { 1, 2, 3, 4 };
        byte[] payload = Enumerable.Repeat((byte)'b', length).ToArray();
        byte[] encoded = new byte[length];
        for (int i = 0; i < length; i++) encoded[i] = (byte)(payload[i] ^ mask[i % 4]);

        var frame = new byte[2 + 2 + 4 + length];
        frame[0] = opcodeFin; frame[1] = firstLen;
        frame[2] = (byte)(length >> 8); frame[3] = (byte)(length & 0xFF);
        Array.Copy(mask, 0, frame, 4, 4);
        Array.Copy(encoded, 0, frame, 8, length);

        var df = await Parse(frame);
        Assert.NotNull(df);
        Assert.Equal((ulong)length, df!.Length);
        Assert.Equal(new string('b', length), df.Message);
    }

    [Fact]
    public async Task Parse_64Bit_Length()
    {
        ulong length = 70000;
        byte opcodeFin = 0x81;
        byte firstLen = 0xFF; // 127 + mask bit
        byte[] mask = { 1, 2, 3, 4 };
        byte[] payload = Enumerable.Repeat((byte)'c', (int)length).ToArray();
        byte[] encoded = new byte[length];
        for (int i = 0; i < (int)length; i++) encoded[i] = (byte)(payload[i] ^ mask[i % 4]);

        var frame = new byte[2 + 8 + 4 + length];
        frame[0] = opcodeFin; frame[1] = firstLen;
        ulong l = length;
        frame[2] = (byte)(l >> 56); frame[3] = (byte)(l >> 48); frame[4] = (byte)(l >> 40); frame[5] = (byte)(l >> 32);
        frame[6] = (byte)(l >> 24); frame[7] = (byte)(l >> 16); frame[8] = (byte)(l >> 8); frame[9] = (byte)(l & 0xFF);
        Array.Copy(mask, 0, frame, 10, 4); Array.Copy(encoded, 0, frame, 14, (int)length);

        var df = await Parse(frame);
        Assert.NotNull(df);
        Assert.Equal(length, df!.Length);
        Assert.Equal(new string('c', (int)length), df.Message);
    }
}
