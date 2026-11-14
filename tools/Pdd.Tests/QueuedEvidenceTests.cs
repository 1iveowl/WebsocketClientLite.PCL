using System.Text.Json.Nodes;
using System.Xml.Linq;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class QueuedEvidenceTests
{
    static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static JsonNode Run(bool pass) => Data.Node(new CommandReceipt { Status = pass ? "passed" : "failed", ExitCode = pass ? 0 : 1, StartedAt = Data.Now(), FinishedAt = Data.Now() });
    static JsonNode Fixture(string directory)
    {
        foreach (var phase in new[] { "baseline", "mutant" })
        {
            var path = Path.Combine(directory, phase); Directory.CreateDirectory(path);
            var names = new[] { Evidence.QueuedTest.Split('.').Last(), "RecordHostRuntime" };
            new XDocument(new XElement(Ns + "TestRun",
                new XElement(Ns + "TestDefinitions", names.Select(n => new XElement(Ns + "UnitTest", new XAttribute("id", n), new XElement(Ns + "TestMethod", new XAttribute("name", n))))),
                new XElement(Ns + "Results", names.Select((n, i) => new XElement(Ns + "UnitTestResult", new XAttribute("testId", n),
                    new XAttribute("outcome", phase == "mutant" && i == 0 ? "Failed" : "Passed"),
                    new XElement(Ns + "Output", i == 0 ? "Overlapping frame delegates" : "PDD_HOST_RUNTIME=.NET 10 fixture")))))).Save(Path.Combine(path, "results.trx"));
            Data.Save(Path.Combine(path, "execution.json"), new { exit_code = phase == "baseline" ? 0 : 1, timed_out = false, interrupted = false });
        }
        return new JsonObject { ["status"] = "passed", ["library_target"] = Evidence.PrimaryTfm, ["host_runtime"] = ".NET 10 fixture", ["baseline"] = Run(true), ["mutant"] = Run(false),
            ["mutation"] = new JsonObject { ["file"] = Evidence.QueuedSource, ["before"] = Evidence.QueuedBefore, ["after"] = Evidence.QueuedAfter, ["original_hash"] = Hash, ["mutant_hash"] = new string('b', 64) } };
    }
    [Fact]
    public void NamedOverlapAssertionIsAccepted()
    {
        using var tree = new TemporaryTree(); Evidence.ValidateQueuedReport(tree.Path, Fixture(tree.Path), Hash);
    }
    [Theory]
    [InlineData("baseline")]
    [InlineData("source")]
    [InlineData("edit")]
    [InlineData("runtime")]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("compile")]
    [InlineData("survival")]
    [InlineData("assertion")]
    [InlineData("missing-case")]
    public void InvalidEvidenceCannotCountAsDetection(string failure)
    {
        using var tree = new TemporaryTree(); var report = Fixture(tree.Path);
        var trx = Path.Combine(tree.Path, "mutant/results.trx");
        switch (failure)
        {
            case "baseline": report["baseline"] = Run(false); break;
            case "source": report["mutation"]!["original_hash"] = "stale"; break;
            case "edit": report["mutation"]!["after"] = "other"; break;
            case "runtime": report["host_runtime"] = "other"; break;
            case "survival": report["mutant"] = Run(true); break;
            case "compile": File.Delete(trx); break;
            case "assertion": File.WriteAllText(trx, File.ReadAllText(trx).Replace("Overlapping frame delegates", "Stuck owner")); break;
            case "missing-case":
                var xml = XDocument.Load(trx); xml.Descendants(Ns + "UnitTestResult").First().Remove(); xml.Save(trx); break;
            default:
                var execution = Data.Read(Path.Combine(tree.Path, "mutant/execution.json"));
                execution[failure == "timeout" ? "timed_out" : "interrupted"] = true;
                Data.Save(Path.Combine(tree.Path, "mutant/execution.json"), execution); break;
        }
        Assert.ThrowsAny<Exception>(() => Evidence.ValidateQueuedReport(tree.Path, report, Hash));
    }
}
