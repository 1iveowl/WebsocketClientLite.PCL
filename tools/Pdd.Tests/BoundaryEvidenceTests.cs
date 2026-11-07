using System.Text.Json.Nodes;
using System.Xml.Linq;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class BoundaryEvidenceTests
{
    static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static JsonObject Run(bool pass) => Data.Node(new CommandReceipt { Status = pass ? "passed" : "failed", ExitCode = pass ? 0 : 1, StartedAt = Data.Now(), FinishedAt = Data.Now() }).AsObject();
    static XElement Result(string name, bool pass, string message) => new(Ns + "UnitTestResult",
        new XAttribute("testName", "WebsocketClientLiteTest.CloseBoundaryTests." + name), new XAttribute("outcome", pass ? "Passed" : "Failed"), new XElement(Ns + "Output", message));
    static void Trx(string directory, IEnumerable<XElement> results)
    {
        Directory.CreateDirectory(directory);
        new XDocument(new XElement(Ns + "TestRun", new XElement(Ns + "Results", results))).Save(Path.Combine(directory, "results.trx"));
    }
    static JsonObject Fixture(string directory)
    {
        var baseline = Evidence.BoundaryCases.Select(c => c.Test).Distinct().Select(t => Result(t, true, "")).ToList();
        baseline.Add(Result("DisposalStopsActivePeriodicPingsBeforeClose", true, ""));
        baseline.Add(Result("RecordHostRuntime", true, "PDD_HOST_RUNTIME=.NET 10 fixture"));
        Trx(Path.Combine(directory, "baseline"), baseline);
        var entries = new JsonArray();
        foreach (var c in Evidence.BoundaryCases)
            for (int repeat = 0; repeat < (c.Id == "read-failure" ? 5 : 1); repeat++)
            {
                var name = c.Id + "-" + repeat;
                var rows = new List<XElement> { Result(c.Test, false, c.Assertion), Result("RecordHostRuntime", true, "PDD_HOST_RUNTIME=.NET 10 fixture") };
                if (c.Test == "DisposalStopsActivePeriodicPingsBeforeClose") rows.Add(Result(c.Test, true, ""));
                Trx(Path.Combine(directory, name), rows);
                entries.Add(new JsonObject { ["id"] = name, ["status"] = "passed", ["source_hash"] = Hash, ["mutant_hash"] = new string('b', 64),
                    ["test"] = c.Test, ["assertion"] = c.Assertion, ["before"] = c.Before, ["after"] = c.After, ["run"] = Run(false), ["host_runtime"] = ".NET 10 fixture" });
            }
        return new JsonObject { ["status"] = "passed", ["library_target"] = "net10.0", ["baseline"] = Run(true), ["experiments"] = entries };
    }
    [Fact]
    public void CompleteAssertionEvidenceIsAccepted()
    {
        using var tree = new TemporaryTree();
        Evidence.ValidateBoundaryReport(tree.Path, Fixture(tree.Path), Hash);
    }
    [Theory]
    [InlineData("missing-repeat")]
    [InlineData("duplicate")]
    [InlineData("baseline-failed")]
    [InlineData("wrong-source")]
    [InlineData("wrong-edit")]
    [InlineData("wrong-host")]
    [InlineData("timeout")]
    [InlineData("compile-error")]
    [InlineData("unrelated-assertion")]
    [InlineData("missing-case")]
    public void IncompleteOrUnrelatedEvidenceCannotPass(string failure)
    {
        using var tree = new TemporaryTree();
        var report = Fixture(tree.Path);
        var entries = report["experiments"]!.AsArray();
        var first = entries[0]!;
        switch (failure)
        {
            case "missing-repeat": entries.RemoveAt(0); break;
            case "duplicate": entries[1] = first.DeepClone(); break;
            case "baseline-failed": report["baseline"] = Run(false); break;
            case "wrong-source": first["source_hash"] = "other"; break;
            case "wrong-edit": first["after"] = "different"; break;
            case "wrong-host": first["host_runtime"] = "other"; break;
            case "timeout": first["run"]!["exit_code"] = 137; break;
            case "compile-error": File.Delete(Path.Combine(tree.Path, "read-failure-0/results.trx")); break;
            case "unrelated-assertion": Trx(Path.Combine(tree.Path, "read-failure-0"), [Result(first["test"]!.ToString(), false, "TimeoutException"), Result("RecordHostRuntime", true, "PDD_HOST_RUNTIME=.NET 10 fixture")]); break;
            case "missing-case": Trx(Path.Combine(tree.Path, "baseline"), [Result("RecordHostRuntime", true, "PDD_HOST_RUNTIME=.NET 10 fixture")]); break;
        }
        Assert.ThrowsAny<Exception>(() => Evidence.ValidateBoundaryReport(tree.Path, report, Hash));
    }
}
