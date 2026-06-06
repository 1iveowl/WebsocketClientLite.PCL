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
    public void CreateMaskKey_ReturnsFourBytes()
    {
        var key = WebsocketMasking.CreateMaskKey();
        Assert.Equal(4, key.Length);
    }
}
