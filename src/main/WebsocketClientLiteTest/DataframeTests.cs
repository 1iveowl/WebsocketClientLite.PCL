using System.Text;
using IWebsocketClientLite;
using WebsocketClientLite.Model;
using Xunit;

namespace WebsocketClientLiteTest;

public class DataframeTests
{
    [Fact]
    public void Message_AfterWithClone_ReflectsNewPayload()
    {
        var first = new Dataframe
        {
            Opcode = OpcodeKind.Text,
            Payload = Encoding.UTF8.GetBytes("abc"),
        };

        // Read Message BEFORE cloning — with a cached field this would poison the
        // clone with the stale "abc" value.
        Assert.Equal("abc", first.Message);

        var merged = first with { Payload = Encoding.UTF8.GetBytes("abcdefghi") };

        Assert.Equal("abcdefghi", merged.Message);
        Assert.Equal("abc", first.Message); // original unaffected
    }

    [Fact]
    public void Message_NonTextOpcode_IsNull()
    {
        var binary = new Dataframe
        {
            Opcode = OpcodeKind.Binary,
            Payload = new byte[] { 1, 2, 3 },
        };

        Assert.Null(binary.Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, binary.Binary);
    }
}
