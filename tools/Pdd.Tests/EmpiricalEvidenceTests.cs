using System.Text;
using System.Text.Json.Nodes;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class EmpiricalEvidenceTests
{
    [Theory]
    [InlineData("Version: 5.0.0\n", true)]
    [InlineData("Version: \u001b[38;5;2m5.0.0\u001b[0m\r\n", true)]
    [InlineData("Version: 5.0.0-preview", false)]
    [InlineData("Version: 5.0.1", false)]
    [InlineData("Tool 'dotnet-stryker' (version '5.0.0') was restored.", false)]
    public void VersionProbeAcceptsCiColorButRejectsWrongOrMissingVersion(string log, bool expected) =>
        Assert.Equal(expected, Evidence.HasPinnedStrykerVersion(log));

    static (JsonNode Report, JsonNode Dispositions, string Hash) Fixture()
    {
        var hash = Data.Hash(Encoding.UTF8.GetBytes("source"));
        JsonNode Mutant(string id, string status) => Data.Node(new { id, status, mutatorName = "Boolean", location = new { start = new { line = int.Parse(id), column = 1 }, end = new { line = int.Parse(id), column = 2 } }, replacement = "false" });
        var mutants = new JsonArray(Mutant("1", "Killed"), Mutant("2", "Survived"), Mutant("3", "NoCoverage"), Mutant("4", "Timeout"), Mutant("5", "CompileError"));
        var dispositions = new JsonArray(mutants.Skip(1).Take(3).Select(m => Data.Node(new { key = Evidence.MutantKey(m!), classification = "equivalent", reason = "Fixture rationale only, not a real disposition." })).ToArray());
        return (new JsonObject { ["files"] = new JsonObject { ["Service/WebsocketConnectionHandler.cs"] = new JsonObject { ["source"] = "source", ["mutants"] = mutants } } }, new JsonObject { ["source_hash"] = hash, ["mutants"] = dispositions }, hash);
    }
    [Fact]
    public void RawOutcomesStaySeparate()
    {
        var (report, dispositions, hash) = Fixture();
        var result = Evidence.TriageStryker(report, dispositions, hash);
        foreach (var status in new[] { "Killed", "Survived", "NoCoverage", "Timeout", "CompileError" })
            Assert.Equal(1, result["counts"]![status]!.GetValue<int>());
    }
    [Theory]
    [InlineData("missing-disposition")]
    [InlineData("stale-source")]
    [InlineData("stale-triage")]
    [InlineData("duplicate-id")]
    [InlineData("pending")]
    [InlineData("empty")]
    [InlineData("unresolved-gap")]
    [InlineData("critical-timeout")]
    public void InvalidMutationEvidenceFailsClosed(string mode)
    {
        var (report, dispositions, hash) = Fixture();
        var mutants = report["files"]!["Service/WebsocketConnectionHandler.cs"]!["mutants"]!.AsArray();
        switch (mode)
        {
            case "missing-disposition": dispositions["mutants"]!.AsArray().RemoveAt(0); break;
            case "stale-source": report["files"]!["Service/WebsocketConnectionHandler.cs"]!["source"] = "different"; break;
            case "stale-triage": dispositions["source_hash"] = "different"; break;
            case "duplicate-id": mutants.Add(mutants[0]!.DeepClone()); break;
            case "pending": mutants[0]!["status"] = "Pending"; break;
            case "empty": mutants.Clear(); break;
            case "critical-timeout": dispositions["required_killed"] = new JsonArray(Evidence.MutantKey(mutants[3]!)); break;
            case "unresolved-gap": dispositions["mutants"]![0]!["classification"] = "missing-test"; break;
        }
        Assert.ThrowsAny<Exception>(() => Evidence.TriageStryker(report, dispositions, hash));
    }
    [Fact]
    public void WrongRevisionOfControlledOrStrykerJobIsRejected()
    {
        using var tree = new TemporaryTree();
        foreach (var name in new[] { "controlled", "stryker" })
            Data.Save(Path.Combine(tree.Path, name, "job.json"), new JobReceipt { Job = name, Source = new("wrong", null, false, new()), SourceAfter = new("wrong", null, false, new()) });
        var receipt = new Evidence(RequirementsTests.Root).Aggregate(tree.Path, new("expected", null, false, new()));
        Assert.Equal("failed", receipt["gate"]!["status"]!.ToString());
        Assert.Equal(2, receipt["rejected_jobs"]!.AsObject().Count);
        Assert.Empty(receipt["jobs"]!.AsObject());
    }
    [Fact]
    public void OneTheoryRowCannotStandInForBothControlledSchedules()
    {
        using var tree = new TemporaryTree();
        ReceiptTests.CopyTree(Path.Combine(RequirementsTests.Root, "verification/tests/fixtures/evidence/tests"), tree.Path);
        var path = Path.Combine(tree.Path, "tests/net10.0/results.trx");
        var document = System.Xml.Linq.XDocument.Load(path);
        System.Xml.Linq.XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var definition = new System.Xml.Linq.XElement(document.Descendants(ns + "UnitTest").First());
        definition.SetAttributeValue("id", "controlled");
        definition.Element(ns + "TestMethod")!.SetAttributeValue("className", "WebsocketClientLiteTest.ControlledCloseTests");
        definition.Element(ns + "TestMethod")!.SetAttributeValue("name", Evidence.CriticalTest.Split('.').Last());
        document.Descendants(ns + "TestDefinitions").Single().Add(definition);
        var result = new System.Xml.Linq.XElement(document.Descendants(ns + "UnitTestResult").First());
        result.SetAttributeValue("testId", "controlled");
        result.SetAttributeValue("outcome", "Passed");
        document.Descendants(ns + "Results").Single().Add(result);
        document.Save(path);
        var matrix = Evidence.TestResults(tree.Path);
        Assert.Equal("failed", matrix["net10.0"]!["tests"]![Evidence.CriticalTest]!.ToString());
    }
    [Fact]
    public void InfrastructureFailureIsNotAnOrderingDetection()
    {
        var baseline = Data.Node(new CommandReceipt { Status = "passed", ExitCode = 0, StartedAt = Data.Now(), FinishedAt = Data.Now() });
        foreach (var exit in new int?[] { null, 0, 137 })
            Assert.Throws<InvalidDataException>(() => Evidence.ValidateDefect("missing", baseline, Data.Node(new CommandReceipt { Status = "failed", ExitCode = exit })));
    }
}
