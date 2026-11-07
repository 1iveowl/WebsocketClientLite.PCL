using System.Linq;
using System.Text;
using WebsocketClientLite.Helper;
using Xunit;

namespace WebsocketClientLiteTest;

public class MaskingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("hello world payload")]
    [InlineData("four")]
    [InlineData("a slightly longer payload that spans many mask key repetitions")]
    public void Decode_UnmasksInPlace_AndRoundTrips(string text)
    {
        byte[] key = { 0x11, 0x22, 0x33, 0x44 };
        var original = Encoding.UTF8.GetBytes(text);

        // Mask the same way a server/client would (XOR with key, repeating every 4).
        var masked = original.Select((b, i) => (byte)(b ^ key[i % 4])).ToArray();

        var result = WebsocketMasking.Decode(masked, key);

        Assert.Same(masked, result);    // unmasks in place, no new allocation
        Assert.Equal(original, result); // and recovers the original bytes
    }

    [Fact]
    public void Decode_MatchesRfc6455HelloVector()
    {
        byte[] key = { 0x37, 0xfa, 0x21, 0x3d };
        byte[] payload = { 0x7f, 0x9f, 0x4d, 0x51, 0x58 };
        Assert.Equal(Encoding.ASCII.GetBytes("Hello"), WebsocketMasking.Decode(payload, key));
        Assert.Equal(new byte[] { 0x37, 0xfa, 0x21, 0x3d }, key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(65536)]
    public void Decode_PreservesScratchKeyAndRoundTrips(int length)
    {
        byte[] key = { 0, 255, 128, 1, 99, 98, 97, 96 };
        var savedKey = key.ToArray();
        var original = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var payload = original.ToArray();
        var expected = original.Select((b, i) => (byte)(b ^ key[i % 4])).ToArray();
        Assert.Same(payload, WebsocketMasking.Decode(payload, key));
        Assert.Equal(expected, payload);
        WebsocketMasking.Decode(payload, key);
        Assert.Equal(original, payload);
        Assert.Equal(savedKey, key);
    }

    [Fact]
    public void Decode_CoversEveryByteAndKeyValueAtEachKeyPosition()
    {
        var payload = new byte[1024];
        for (int keyValue = 0; keyValue < 256; keyValue++)
        {
            byte[] key = { (byte)keyValue, (byte)(keyValue + 1), (byte)(keyValue + 2), (byte)(keyValue + 3) };
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i / 4);
            WebsocketMasking.Decode(payload, key);
            for (int i = 0; i < payload.Length; i++)
                Assert.Equal((byte)((byte)(i / 4) ^ key[i % 4]), payload[i]);
        }
    }

    [Fact]
    public void Decode_RejectsInvalidAdapterInputsBeforeMutation()
    {
        byte[] payload = { 1, 2, 3, 4 };
        Assert.Throws<System.ArgumentNullException>(() => WebsocketMasking.Decode(null!, payload));
        Assert.Throws<System.ArgumentNullException>(() => WebsocketMasking.Decode(payload, null!));
        Assert.Throws<System.ArgumentException>(() => WebsocketMasking.Decode(payload, new byte[3]));
        Assert.Throws<System.ArgumentException>(() => WebsocketMasking.Decode(payload, payload));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, payload);
    }

    [Fact]
    public void CreateMaskKey_ReturnsFourBytes()
    {
        var key = WebsocketMasking.CreateMaskKey();
        Assert.Equal(4, key.Length);
    }
}
