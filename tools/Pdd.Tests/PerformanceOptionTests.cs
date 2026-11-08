using Pdd;
using Xunit;

namespace Pdd.Tests;

public class PerformanceOptionTests
{
    [Theory]
    [InlineData("tests", "4")]
    [InlineData("protocol", "4")]
    [InlineData("controlled", "4")]
    [InlineData("stryker", "0")]
    [InlineData("stryker", "-1")]
    [InlineData("stryker", "1.5")]
    [InlineData("stryker", "many")]
    public async Task InvalidWorkerOverrideCannotStartOrCreateEvidence(string job, string workers)
    {
        using var tree = new TemporaryTree();
        var output = Path.Combine(tree.Path, "evidence");
        var run = await Processes.Run(["dotnet", typeof(Program).Assembly.Location, "capture", "--job", job,
            "--stryker-concurrency", workers, "--output", output], RequirementsTests.Root, Path.Combine(tree.Path, "process"), 10);
        Assert.Equal(2, run.ExitCode);
        Assert.False(run.TimedOut);
        Assert.False(Directory.Exists(output));
        Assert.Contains("--stryker-concurrency", run.Stderr);
    }
}
