using System.Text.Json.Nodes;
using System.Xml.Linq;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class CloseCallerEvidenceTests
{
    static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static JsonNode Run(bool pass) => Data.Node(new CommandReceipt { Command = ["dotnet", "test", "-c", "Release", "-p:LibTfm=" + Evidence.PrimaryTfm], Status = pass ? "passed" : "failed", ExitCode = pass ? 0 : 1, StartedAt = Data.Now(), FinishedAt = Data.Now() });
    static JsonNode Fixture(string directory)
    {
        void Write(string phase, IEnumerable<(string Name, string Outcome, string Message)> rows)
        {
            var path = Path.Combine(directory, phase); Directory.CreateDirectory(path);
            new XDocument(new XElement(Ns + "TestRun", new XElement(Ns + "Results", rows.Select(r =>
                new XElement(Ns + "UnitTestResult", new XAttribute("testName", r.Name), new XAttribute("outcome", r.Outcome), new XElement(Ns + "Output", r.Message)))))).Save(Path.Combine(path, "results.trx"));
            Data.Save(Path.Combine(path, "execution.json"), new { exit_code = phase == "baseline" ? 0 : 1, timed_out = false, interrupted = false });
        }
        var runtime = ("RecordHostRuntime", "Passed", "PDD_HOST_RUNTIME=.NET 11 fixture");
        var tests = Evidence.CloseCallerCases.Select(c => (c.Class, c.Test, c.Rows)).Distinct();
        Write("baseline", tests.SelectMany(t => Enumerable.Range(0, t.Rows).Select(i => (t.Class + "." + t.Test + "(" + i + ")", "Passed", ""))).Append(runtime));
        var report = new JsonObject { ["status"] = "passed", ["library_target"] = Evidence.PrimaryTfm, ["baseline"] = Run(true), ["experiments"] = new JsonArray() };
        foreach (var c in Evidence.CloseCallerCases)
        {
            Write(c.Id, Enumerable.Range(0, c.Rows).Select(i => (c.Class + "." + c.Test + "(" + i + ")", i < c.Failing ? "Failed" : "Passed", i < c.Failing ? c.Assertion : "")).Append(runtime));
            report["experiments"]!.AsArray().Add(new JsonObject { ["id"] = c.Id, ["status"] = "passed", ["test"] = c.Class + "." + c.Test, ["assertion"] = c.Assertion,
                ["source_hash"] = Hash, ["mutant_hash"] = new string('b', 64), ["before"] = c.Before, ["after"] = c.After, ["run"] = Run(false), ["host_runtime"] = ".NET 11 fixture" });
        }
        return report;
    }

    [Fact]
    public void NamedAssertionsAreAccepted()
    {
        using var tree = new TemporaryTree(); Evidence.ValidateCloseCallerReport(tree.Path, Fixture(tree.Path), Hash);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("source")]
    [InlineData("edit")]
    [InlineData("runtime")]
    [InlineData("timeout")]
    [InlineData("timeout-message")]
    [InlineData("compile")]
    [InlineData("survival")]
    [InlineData("assertion")]
    [InlineData("extra-row-failed")]
    [InlineData("missing-case")]
    [InlineData("missing-mutant")]
    public void InvalidEvidenceCannotCountAsDetection(string failure)
    {
        using var tree = new TemporaryTree(); var report = Fixture(tree.Path);
        var entry = report["experiments"]![0]!;
        var first = Evidence.CloseCallerCases[0];
        var trx = Path.Combine(tree.Path, first.Id, "results.trx");
        switch (failure)
        {
            case "baseline": report["baseline"] = Run(false); break;
            case "source": entry["source_hash"] = "stale"; break;
            case "edit": entry["after"] = "other"; break;
            case "runtime": entry["host_runtime"] = "other"; break;
            case "survival": entry["run"] = Run(true); break;
            case "compile": File.Delete(trx); break;
            case "missing-mutant": report["experiments"]!.AsArray().RemoveAt(0); break;
            case "assertion": File.WriteAllText(trx, File.ReadAllText(trx).Replace(first.Assertion, "Stuck owner")); break;
            case "timeout-message": File.WriteAllText(trx, File.ReadAllText(trx).Replace(first.Assertion, first.Assertion + " System.TimeoutException")); break;
            case "extra-row-failed":
                // peer-close-skipped expects one of two rows to fail; a second failing row is another defect.
                var two = Evidence.CloseCallerCases.First(c => c.Rows > c.Failing);
                var path = Path.Combine(tree.Path, two.Id, "results.trx");
                var doc = XDocument.Load(path);
                foreach (var r in doc.Descendants(Ns + "UnitTestResult").Where(r => ((string?)r.Attribute("testName"))!.Contains(two.Test))) r.SetAttributeValue("outcome", "Failed");
                doc.Save(path); break;
            case "missing-case":
                var baseline = Path.Combine(tree.Path, "baseline/results.trx");
                var xml = XDocument.Load(baseline); xml.Descendants(Ns + "UnitTestResult").First().Remove(); xml.Save(baseline); break;
            default:
                var execution = Data.Read(Path.Combine(tree.Path, first.Id, "execution.json"));
                execution["timed_out"] = true;
                Data.Save(Path.Combine(tree.Path, first.Id, "execution.json"), execution); break;
        }
        Assert.ThrowsAny<Exception>(() => Evidence.ValidateCloseCallerReport(tree.Path, report, Hash));
    }
}
