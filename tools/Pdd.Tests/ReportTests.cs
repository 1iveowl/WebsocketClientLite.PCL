using Pdd;
using Xunit;

namespace Pdd.Tests;

public class ReportTests
{
    [Fact]
    public async Task RealHistoryUsesMergeBaseAndIncludesAllReviewSources()
    {
        using var tree = new TemporaryTree();
        var root = tree.Path;
        Task<string> Git(params string[] args) => ContractReport.Git(root, args);
        void Put(string name, string text)
        {
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        async Task Commit()
        {
            await Git("add", ".");
            await Git("commit", "-m", "chore: fixture");
        }
        await Git("init", "-b", "target");
        await Git("config", "user.name", "Test Fixture");
        await Git("config", "user.email", "fixture@example.invalid");
        Put("verification/requirements.json", "{\"requirements\":[{\"id\":\"CLOSE-001\",\"intent\":\"original\"}]}");
        Put("verification/boundaries.json", "{\"assumption\":\"original\"}\n");
        Put("verification/trusted-boundaries.md", "original external audit\n");
        Put("verification/Core.dfy", "function Helper(): bool { true }\nmethod Check()\n requires true\n ensures true\n{}\n");
        await Commit();
        var baseline = (await Git("rev-parse", "HEAD")).Trim();
        await Git("switch", "-c", "topic");
        Assert.Contains("No committed changes", await ContractReport.Report(root, "target", "HEAD"));
        Put("verification/requirements.json", "{\"requirements\":[{\"id\":\"CLOSE-001\",\"intent\":\"changed intent\"}]}");
        Put("verification/boundaries.json", "{\"assumption\":\"expanded trust\"}\n");
        Put("verification/trusted-boundaries.md", "changed external audit\n");
        Put("verification/Core.dfy", "function Helper(): bool { false }\nmethod Check()\n requires false\n ensures false\n{}\nmethod {:extern} Native()\n ensures true\n");
        Put("adapter.cs", "// changed caller outside extracted summaries\n");
        await Commit();
        var head = (await Git("rev-parse", "HEAD")).Trim();
        await Git("switch", "target");
        Put("target-only.txt", "must not be reported as a topic deletion\n");
        await Commit();
        await Git("switch", "topic");
        Put("adapter.cs", "// uncommitted sentinel\n");
        Put("core.12592", "untracked sentinel\n");
        var report = await ContractReport.Report(root, "target", "topic");
        foreach (var expected in new[] { baseline, head, "changed intent", "expanded trust", "changed external audit", "+function Helper(): bool { false }", "+ requires false", "+ ensures false", "+method {:extern} Native()", "changed caller outside extracted summaries", "Semantic review required" })
            Assert.Contains(expected, report);
        foreach (var excluded in new[] { "target-only.txt", "uncommitted sentinel", "untracked sentinel", "core.12592" })
            Assert.DoesNotContain(excluded, report);
        File.Delete(Path.Combine(root, "verification/Core.dfy"));
        Put("new.dfy", "predicate Replacement() { true }\n");
        await Commit();
        report = await ContractReport.Report(root, "target", "HEAD");
        Assert.Contains("-function Helper()", report);
        Assert.Contains("+predicate Replacement()", report);
        await Assert.ThrowsAsync<InvalidDataException>(() => ContractReport.Report(root, "missing", "HEAD"));
        Put("verification/requirements.json", "{");
        await Commit();
        await Assert.ThrowsAnyAsync<Exception>(() => ContractReport.Report(root, "target", "HEAD"));
        await Git("checkout", "--orphan", "unrelated");
        await Git("commit", "-m", "chore: independent fixture");
        await Assert.ThrowsAsync<InvalidDataException>(() => ContractReport.Report(root, "target", "HEAD"));
    }
    [Fact] public void EmbeddedFenceIsPreserved() => Assert.StartsWith("````diff", ContractReport.Fence("```\nsource\n```"));
    [Theory]
    [InlineData("capture", "--job", "other")]
    [InlineData("test-target", "--tfm", "other")]
    [InlineData("mutations", "--baseline-budget", "invalid")]
    [InlineData("mutations", "--verification-time-limit", "invalid")]
    public async Task CliUsageErrorsRemainExitTwo(string command, string option, string value)
    {
        using var tree = new TemporaryTree();
        var result = await Processes.Run(["dotnet", typeof(Program).Assembly.Location, command, option, value], tree.Path, Path.Combine(tree.Path, "logs"), 30);
        Assert.Equal(2, result.ExitCode);
    }
    [Fact]
    public async Task CliValidationOutsideRepositoryAndMissingArguments()
    {
        using var tree = new TemporaryTree();
        var dll = typeof(Program).Assembly.Location;
        var result = await Processes.Run(["dotnet", dll, "validate"], tree.Path, Path.Combine(tree.Path, "valid"), 30);
        Assert.True(result.Success, result.Stderr);
        var bad = Path.Combine(tree.Path, "bad.json");
        File.WriteAllText(bad, "{");
        result = await Processes.Run(["dotnet", dll, "validate", bad], tree.Path, Path.Combine(tree.Path, "invalid"), 30);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("Validated", result.Stdout);
        result = await Processes.Run(["dotnet", dll, "contract-report", "--output", Path.Combine(tree.Path, "report.md")], tree.Path, Path.Combine(tree.Path, "missing"), 30);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--target", result.Stderr);
        Assert.False(File.Exists(Path.Combine(tree.Path, "report.md")));
    }
}
