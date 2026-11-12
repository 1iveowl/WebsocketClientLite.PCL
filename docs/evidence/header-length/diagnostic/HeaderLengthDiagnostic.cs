// DIAGNOSTIC ONLY (2026-11-11). Not a regression test and not part of the replay.
// Records what the parser does with the two non-minimal length headers, with and
// without the payload bytes the header announces. It asserts nothing.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebsocketClientLite.Helper;
using WebsocketClientLite.Service;
using Xunit;
using Xunit.Abstractions;

namespace WebsocketClientLiteTest;

public class HeaderLengthDiagnostic(ITestOutputHelper output)
{
    private sealed class Fake : TcpConnectionService
    {
        public Fake(byte[] bytes) : base(() => false, (o, c, ch, e) => true,
            (client, uri) => Task.CompletedTask, (status, ex) => { }, true,
            new System.Net.Sockets.TcpClient(), 0)
        {
            typeof(TcpConnectionService).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(this, new MemoryStream(bytes));
        }
    }

    [Theory]
    [InlineData("16-bit field carrying 125", new byte[] { 0x81, 126, 0, 125 }, 125)]
    [InlineData("64-bit field carrying 65535", new byte[] { 0x81, 127, 0, 0, 0, 0, 0, 0, 255, 255 }, 65535)]
    public async Task Record(string name, byte[] header, int announced)
    {
        foreach (var withPayload in new[] { false, true })
        {
            var bytes = withPayload ? header.Concat(Enumerable.Repeat((byte)'a', announced)).ToArray() : header;
            string outcome;
            try
            {
                var df = await new Fake(bytes).ReadDataframeAsync(CancellationToken.None);
                outcome = df is null ? "returned null" : $"ACCEPTED: Length={df.Length}, Opcode={df.Opcode}, FIN={df.FIN}, payload bytes={df.Payload?.Length}";
            }
            catch (Exception ex)
            {
                outcome = $"threw {ex.GetType().Name}: {ex.Message}";
            }
            output.WriteLine($"{name}; payload appended={withPayload}; total bytes={bytes.Length}; {outcome}");
        }
    }
}
