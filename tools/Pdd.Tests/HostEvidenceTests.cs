using System.Text.Json.Nodes;
using System.Xml.Linq;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class HostEvidenceTests
{
    static HostCell Cell => Evidence.FindHostCell("linux-10-net10.0");
    static SourceIdentity Source => new("revision", null, false, new() { ["source.cs"] = "hash" });
    static JsonNode Host => Data.Parse("""
        {"runtime":".NET 10.0.14","runtime_version":"10.0.14","test_framework":".NETCoreApp,Version=v10.0",
         "os":"linux","os_description":"Ubuntu 24.04.1 LTS","os_version":"Unix 6.8.0.0","os_release":"ID=ubuntu\nVERSION_ID=\"24.04\"\n",
         "os_architecture":"X64","process_architecture":"X64","library_framework":".NETCoreApp,Version=v10.0",
         "library_name":"WebsocketClientLite, Version=1.0.0.0","library_path":"/checkout/bin/WebsocketClientLite.dll",
         "library_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","library_mvid":"dcac2be4-d8bc-40f8-be82-b8f8f39f728c"}
        """);
    static void Fixture(string root, JsonNode host)
    {
        var directory = Path.Combine(root, Cell.Id);
        var target = Path.Combine(directory, "tests", Cell.LibraryTfm);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(directory, "expected-library.dll"), "fixture assembly bytes");
        host["library_sha256"] = Data.Hash(File.ReadAllBytes(Path.Combine(directory, "expected-library.dll")));
        var report = new JobReceipt { Job = Cell.Id, Source = Source, SourceAfter = Source, FinishedAt = Data.Now() };
        report.Tools["cell"] = Data.Node(Cell);
        foreach (var name in new[] { "sdk", "restore", "build", "test" })
        {
            var path = name == "test" ? target : Path.Combine(directory, name);
            Directory.CreateDirectory(path);
            var command = new[] { "dotnet", name, "-p:LibTfm=net10.0", "-p:TestHostTfm=net10.0", "-c", "Release" };
            var run = new CommandReceipt { Command = command, Status = "passed", ExitCode = 0, StartedAt = Data.Now(), FinishedAt = Data.Now() };
            report.Runs[name] = run;
            Data.Save(Path.Combine(path, "run.json"), run);
            Data.Save(Path.Combine(path, "execution.json"), new ExecutionResult { Command = command, ExitCode = 0, ElapsedSeconds = 1 });
            File.WriteAllText(Path.Combine(path, "output.log"), "fixture");
        }
        Data.Save(Path.Combine(directory, "job.json"), report);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var definitions = new XElement(ns + "TestDefinitions");
        var results = new XElement(ns + "Results");
        void Add(string name, int count, string? output = null)
        {
            var id = definitions.Elements().Count().ToString();
            var split = name.LastIndexOf('.');
            definitions.Add(new XElement(ns + "UnitTest", new XAttribute("id", id), new XElement(ns + "TestMethod", new XAttribute("className", name[..split]), new XAttribute("name", name[(split + 1)..]))));
            for (var i = 0; i < count; i++)
                results.Add(new XElement(ns + "UnitTestResult", new XAttribute("testId", id), new XAttribute("outcome", "Passed"), new XElement(ns + "Output", new XElement(ns + "StdOut", output))));
        }
        foreach (var (prefix, cases) in new[] { ("QueuedWriteTests.", Evidence.QueuedCaseCounts), ("PayloadLeaseTests.", Evidence.LeaseCaseCounts), ("ParserRetentionTests.", Evidence.RetentionCaseCounts), ("ReconnectTests.", Evidence.ReconnectCaseCounts) })
            foreach (var (name, count) in cases) Add("WebsocketClientLiteTest." + prefix + name, count);
        Add("WebsocketClientLiteTest.RuntimeEvidenceTests.RecordHostRuntime", 1, "PDD_HOST_RUNTIME=.NET 10.0.14\nPDD_HOST_IDENTITY=" + host.ToJsonString());
        Add("Fixture.OtherSuite", 269 - results.Elements().Count());
        new XDocument(new XElement(ns + "TestRun", definitions, results)).Save(Path.Combine(target, "results.trx"));
    }
    [Fact]
    public async Task CaptureReadbackClosesWritersBeforeReading()
    {
        using var tree = new TemporaryTree();
        var result = await Processes.Run(["dotnet", "--version"], RequirementsTests.Root, tree.Path, 30);
        Assert.True(result.Success, result.Stderr);
        Assert.Equal(Data.Text(Path.Combine(tree.Path, "stdout.log")), result.Stdout);
        Assert.NotEmpty(result.Stdout);
    }
    [Theory]
    [InlineData("macos-10-net10.0", "macos", "macOS 15.8.2", "Unix 15.8.2", "Arm64")]
    [InlineData("windows-10-net10.0", "windows", "Microsoft Windows 10.0.20348", "Microsoft Windows NT 10.0.20348.0", "X64")]
    public void RecognizesQualifiedOsVersionFormats(string id, string os, string description, string version, string arch)
    {
        var host = Host;
        host["os"] = os; host["os_description"] = description; host["os_version"] = version;
        host["os_architecture"] = arch; host["process_architecture"] = arch;
        Evidence.ValidateHostIdentity(Evidence.FindHostCell(id), host, ".NET 10.0.14");
    }
    [Fact]
    public void PositiveSourceBoundHostReceipt()
    {
        using var tree = new TemporaryTree();
        Fixture(tree.Path, Host);
        var receipt = Evidence.HostResults(tree.Path, Source, Data.Node(Evidence.CiIdentity()), [Cell]);
        Assert.True(receipt["gate"]!["status"]!.ToString() == "passed", receipt.ToJsonString());
    }
    [Theory]
    [InlineData("runtime_version", "9.0.22", "runtime")]
    [InlineData("test_framework", ".NETCoreApp,Version=v9.0", "host target")]
    [InlineData("library_framework", ".NETStandard,Version=v2.0", "library target")]
    [InlineData("os", "windows", "OS/architecture")]
    [InlineData("os_release", "ID=ubuntu\nVERSION_ID=\"22.04\"", "OS release")]
    [InlineData("process_architecture", "Arm64", "OS/architecture")]
    [InlineData("library_path", "", "library identity")]
    [InlineData("library_mvid", "", "library identity")]
    public void RejectWrongHostOrAssembly(string key, string value, string reason)
    {
        using var tree = new TemporaryTree();
        var host = Host; host[key] = value;
        Fixture(tree.Path, host);
        var receipt = Evidence.HostResults(tree.Path, Source, Data.Node(Evidence.CiIdentity()), [Cell]);
        Assert.Equal("failed", receipt["gate"]!["status"]!.ToString());
        Assert.Contains(Cell.Id, receipt["errors"]!.ToString());
        Assert.Contains(reason, receipt["errors"]!.ToString());
    }
    [Theory]
    [InlineData("missing", "Could not find")]
    [InlineData("source", "source mismatch")]
    [InlineData("after", "source mismatch")]
    [InlineData("attempt", "run/attempt")]
    [InlineData("run", "run/attempt")]
    [InlineData("assembly", "assembly hash")]
    [InlineData("timeout", "timed out")]
    [InlineData("restore", "restore did not pass")]
    [InlineData("duplicate", "duplicate cell")]
    public void RejectMissingMixedOrFailedEvidence(string mode, string reason)
    {
        using var tree = new TemporaryTree();
        Fixture(tree.Path, Host);
        var directory = Path.Combine(tree.Path, Cell.Id);
        var path = Path.Combine(directory, "job.json");
        var report = Data.Read(path);
        switch (mode)
        {
            case "source": report["source"]!["tested_commit"] = "stale"; break;
            case "after": report["source_after"]!["source_hashes"]!["source.cs"] = "changed"; break;
            case "attempt": report["ci"]!["GITHUB_RUN_ATTEMPT"] = "other"; break;
            case "run": report["ci"]!["GITHUB_RUN_ID"] = "other"; break;
            case "restore": report["runs"]!["restore"]!["status"] = "failed"; break;
            case "assembly": File.WriteAllText(Path.Combine(directory, "expected-library.dll"), "substituted"); break;
            case "timeout":
                var execution = Path.Combine(directory, "tests/net10.0/execution.json");
                var data = Data.Read(execution); data["timed_out"] = true; Data.Save(execution, data); break;
        }
        Data.Save(path, report);
        if (mode == "missing") File.Delete(path);
        var receipt = Evidence.HostResults(tree.Path, Source, Data.Node(Evidence.CiIdentity()), mode == "duplicate" ? [Cell, Cell] : [Cell]);
        Assert.Equal("failed", receipt["gate"]!["status"]!.ToString());
        Assert.Contains(Cell.Id, receipt["errors"]!.ToString());
        Assert.Contains(reason, receipt["errors"]!.ToString());
    }
    [Fact]
    public void MissingMandatoryLinuxCellFailsFullAggregate()
    {
        using var tree = new TemporaryTree();
        var receipt = new Evidence(RequirementsTests.Root).Aggregate(tree.Path, Source);
        Assert.Equal("failed", receipt["gate"]!["status"]!.ToString());
        Assert.Contains("Missing tests job evidence", receipt["errors"]!.ToString());
    }
}
