using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace WebsocketClientLiteTest;

// Emit from inside the test process: installed SDKs do not identify its runtime.
public class RuntimeEvidenceTests
{
    private readonly ITestOutputHelper _output;

    public RuntimeEvidenceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RecordHostRuntime() =>
        _output.WriteLine("PDD_HOST_RUNTIME=" + RuntimeInformation.FrameworkDescription);
}
