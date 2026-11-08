using System.Text.Json.Nodes;
using System.Xml.Linq;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class ReconnectEvidenceTests
{
    static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static readonly Dictionary<string, string?> Hashes = new() { [Evidence.ReconnectSource] = Hash };
    static JsonNode Run(bool pass) => Data.Node(new CommandReceipt { Command = ["dotnet", "test", "-c", "Release", "-p:LibTfm=net10.0"], Status = pass ? "passed" : "failed", ExitCode = pass ? 0 : 1, StartedAt = Data.Now(), FinishedAt = Data.Now() });
    static JsonNode Fixture(string directory)
    {
        void Write(string phase, IEnumerable<(string Name, string Outcome, string Message)> rows)
        {
            var path = Path.Combine(directory, phase); Directory.CreateDirectory(path);
            new XDocument(new XElement(Ns + "TestRun", new XElement(Ns + "Results", rows.Select(r =>
                new XElement(Ns + "UnitTestResult", new XAttribute("testName", r.Name), new XAttribute("outcome", r.Outcome), new XElement(Ns + "Output", r.Message)))))).Save(Path.Combine(path, "results.trx"));
            Data.Save(Path.Combine(path, "execution.json"), new { exit_code = phase == "baseline" ? 0 : 1, timed_out = false, interrupted = false });
        }
        var runtime = ("RecordHostRuntime", "Passed", "PDD_HOST_RUNTIME=.NET 10 fixture");
        Write("baseline", Evidence.ReconnectCaseCounts.SelectMany(c => Enumerable.Range(0, c.Value).Select(i => ("ReconnectTests." + c.Key + "(" + i + ")", "Passed", ""))).Append(runtime));
        var report = new JsonObject { ["status"] = "passed", ["library_target"] = "net10.0", ["baseline"] = Run(true), ["experiments"] = new JsonArray() };
        foreach (var c in Evidence.ReconnectCases)
        {
            Write(c.Id, new[] { (c.Test, "Failed", c.Assertion), runtime });
            report["experiments"]!.AsArray().Add(new JsonObject { ["id"] = c.Id, ["status"] = "passed", ["test"] = c.Test, ["assertion"] = c.Assertion,
                ["source_hash"] = Hash, ["mutant_hash"] = new string('b', 64), ["before"] = c.Before, ["after"] = c.After, ["run"] = Run(false), ["host_runtime"] = ".NET 10 fixture" });
        }
        return report;
    }
    [Fact]
    public void NamedCleanupAssertionIsAccepted()
    {
        using var tree = new TemporaryTree(); Evidence.ValidateReconnectReport(tree.Path, Fixture(tree.Path), Hashes);
    }
    [Theory]
    [InlineData("configuration")]
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
    [InlineData("missing-mutant")]
    [InlineData("duplicate-mutant")]
    public void InvalidEvidenceCannotCountAsDetection(string failure)
    {
        using var tree = new TemporaryTree(); var report = Fixture(tree.Path);
        var entry = report["experiments"]![0]!;
        var trx = Path.Combine(tree.Path, "missing-removal/results.trx");
        switch (failure)
        {
            case "configuration": entry["run"]!["command"]![3] = "Debug"; break;
            case "baseline": report["baseline"] = Run(false); break;
            case "source": entry["source_hash"] = "stale"; break;
            case "edit": entry["after"] = "other"; break;
            case "runtime": entry["host_runtime"] = "other"; break;
            case "survival": entry["run"] = Run(true); break;
            case "compile": File.Delete(trx); break;
            case "missing-mutant": report["experiments"]!.AsArray().RemoveAt(0); break;
            case "duplicate-mutant": report["experiments"]!.AsArray().Add(entry.DeepClone()); break;
            case "assertion": File.WriteAllText(trx, File.ReadAllText(trx).Replace(Evidence.ReconnectCases[0].Assertion, "Stuck owner")); break;
            case "missing-case":
                var path = Path.Combine(tree.Path, "baseline/results.trx");
                var xml = XDocument.Load(path); xml.Descendants(Ns + "UnitTestResult").First().Remove(); xml.Save(path); break;
            default:
                var execution = Data.Read(Path.Combine(tree.Path, "missing-removal/execution.json"));
                execution[failure == "timeout" ? "timed_out" : "interrupted"] = true;
                Data.Save(Path.Combine(tree.Path, "missing-removal/execution.json"), execution); break;
        }
        Assert.ThrowsAny<Exception>(() => Evidence.ValidateReconnectReport(tree.Path, report, Hashes));
    }
}
