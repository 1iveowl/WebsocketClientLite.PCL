using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    public const string LeaseSource = "src/main/WebsocketClientLite/Service/WebsocketSenderHandler.cs";
    public static readonly Dictionary<string, int> LeaseCaseCounts = new()
    {
        ["PausedWriteRetainsLeaseUntilCompletion"] = 1,
        ["InitializedCountMatchesIndependentWireBytes"] = 5,
        ["StableAliasedListInputsRemainUnchanged"] = 1,
        ["GateCancellationReturnsOnlyAcquiredLeases"] = 2,
        ["FailedOrPartialWriteReturnsAfterSettlement"] = 2,
        ["InvalidTextIsRejectedBeforeRent"] = 1,
        ["SuppressedQueuedPongReturnsWithoutDelegateUse"] = 1,
        ["SendObservationTimeoutRetainsPendingLease"] = 1
    };
    public static readonly (string Id, string Before, string After, string Assertion)[] LeaseCases =
    [
        ("early-return", "await SendFrameCoreAsync(buffer, written, opcode, fragment, ct).ConfigureAwait(false);",
            "var pending = SendFrameCoreAsync(buffer, written, opcode, fragment, ct);\n            _framePool.Return(buffer, clearArray: true);\n            await pending.ConfigureAwait(false);", "Lease returned before write completion"),
        ("missing-return", "_framePool.Return(buffer, clearArray: true);", ";", "Lease not returned exactly once"),
        ("wrong-count", "await SendFrameCoreAsync(buffer, written, opcode, fragment, ct).ConfigureAwait(false);",
            "await SendFrameCoreAsync(buffer, buffer.Length, opcode, fragment, ct).ConfigureAwait(false);", "Write count includes uninitialized capacity")
    ];
    public const string LeaseTest = "PausedWriteRetainsLeaseUntilCompletion";
    public async Task<int> LeaseDefects(string output, CancellationToken cancellation)
    {
        Fresh(output);
        var report = new JsonObject { ["status"] = "failed", ["source"] = Node(await Identity()), ["ci"] = Node(CiIdentity()),
            ["started_at"] = Now(), ["library_target"] = PrimaryTfm, ["experiments"] = new JsonArray() };
        using var copy = new TemporaryTree();
        try
        {
            // Restrict to repository inputs; unrelated untracked dumps are never copied/read.
            var paths = (await ContractReport.Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard",
                "src", "verification", "tools", "scripts", ".config", "global.json", "Directory.Build.props", "Directory.Build.targets", "NuGet.config")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths.Distinct())
            {
                var target = Path.Combine(copy.Path, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(root, path), target);
            }
            var runner = new Evidence(copy.Path);
            async Task<CommandReceipt> Run(string name, string filter) => await runner.Execute(
                ["dotnet", "test", Project, "-c", "Release", "-p:LibTfm=" + PrimaryTfm, "--filter", filter + "|FullyQualifiedName=" + RuntimeTest,
                    "--logger", "trx;LogFileName=results.trx", "--results-directory", Path.Combine(output, name)], Path.Combine(output, name), 180, cancellation);
            report["baseline"] = Node(await Run("baseline", "FullyQualifiedName~WebsocketClientLiteTest.PayloadLeaseTests"));
            Require(Passed(report["baseline"]), "Lease baseline failed");
            var file = Path.Combine(copy.Path, LeaseSource);
            var original = Text(file);
            foreach (var c in LeaseCases)
            {
                var name = c.Id;
                var index = original.IndexOf(c.Before, StringComparison.Ordinal);
                Require(index >= 0 && original.IndexOf(c.Before, index + 1, StringComparison.Ordinal) < 0, "Ambiguous/missing lease edit");
                File.WriteAllText(file, original[..index] + c.After + original[(index + c.Before.Length)..]);
                var run = await Run(name, "FullyQualifiedName=WebsocketClientLiteTest.PayloadLeaseTests." + LeaseTest);
                var experiment = new JsonObject { ["id"] = name, ["status"] = "failed", ["test"] = LeaseTest, ["assertion"] = c.Assertion,
                    ["source_hash"] = Hash(System.Text.Encoding.UTF8.GetBytes(original)), ["mutant_hash"] = Hash(File.ReadAllBytes(file)),
                    ["before"] = c.Before, ["after"] = c.After, ["run"] = Node(run) };
                report["experiments"]!.AsArray().Add(experiment);
                experiment["host_runtime"] = ValidateLeaseResult(Path.Combine(output, name), Node(run), c.Assertion);
                experiment["status"] = "passed";
                File.WriteAllText(file, original);
            }
            report["status"] = "passed";
            ValidateLeaseReport(output, report, Hash(System.Text.Encoding.UTF8.GetBytes(original)));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { report["status"] = "failed"; report["error"] = e.Message; }
        finally { report["finished_at"] = Now(); report["source_after"] = Node(await Identity()); Save(Path.Combine(output, "results.json"), report); }
        return S(report, "status") == "passed" ? 0 : 1;
    }

    public static void ValidateLeaseReport(string directory, JsonNode report, string sourceHash)
    {
        Require(S(report, "status") == "passed" && S(report, "library_target") == PrimaryTfm && Passed(report["baseline"]), "Incomplete lease baseline/report");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var baseline = XDocument.Load(Path.Combine(directory, "baseline/results.trx")).Descendants(ns + "UnitTestResult").ToArray();
        Require(baseline.Length == 15 && baseline.All(r => (string?)r.Attribute("outcome") == "Passed"), "Incomplete lease baseline cases");
        foreach (var (test, count) in LeaseCaseCounts)
            Require(baseline.Count(r => ((string?)r.Attribute("testName"))?.Contains("PayloadLeaseTests." + test, StringComparison.Ordinal) == true) == count, "Missing named lease baseline case");
        ValidateLeaseExecution(Path.Combine(directory, "baseline"), 0);
        var baselineRuntime = baseline.Single(r => ((string?)r.Attribute("testName"))?.EndsWith("RecordHostRuntime", StringComparison.Ordinal) == true).Value;
        var entries = report["experiments"]!.AsArray();
        Require(entries.Count == 3, "Missing lease experiments or repetitions");
        foreach (var c in LeaseCases)
        {
            var name = c.Id;
            var e = entries.Single(e => S(e, "id") == name)!;
            Require(S(e, "status") == "passed" && S(e, "source_hash") == sourceHash && S(e, "mutant_hash").Length == 64 && S(e, "mutant_hash") != sourceHash, "Lease mutation identity mismatch");
            Require(S(e, "before") == c.Before && S(e, "after") == c.After && S(e, "test") == LeaseTest && S(e, "assertion") == c.Assertion, "Lease experiment contract mismatch");
            var runtime = ValidateLeaseResult(Path.Combine(directory, name), e["run"]!, c.Assertion);
            Require(S(e, "host_runtime") == runtime && baselineRuntime.Contains("PDD_HOST_RUNTIME=" + runtime, StringComparison.Ordinal), "Lease runtime mismatch");
        }
    }

    private static void ValidateLeaseExecution(string directory, int code)
    {
        var e = Read(Path.Combine(directory, "execution.json"));
        Require(e["timed_out"]?.GetValue<bool>() == false && e["interrupted"]?.GetValue<bool>() == false && e["launch_error"] is null && e["exit_code"]?.ToString() == code.ToString(), "Invalid lease execution");
    }
    private static string ValidateLeaseResult(string directory, JsonNode run, string assertion)
    {
        ValidateLeaseExecution(directory, 1);
        return ValidateBoundaryResult(Path.Combine(directory, "results.trx"), run, LeaseTest, assertion, 1);
    }
    // Manual smoke probe; deliberately not repeated fifty times in every PR job.
    public async Task<int> LeaseProbe(string output, CancellationToken cancellation)
    {
        Fresh(output);
        var report = new System.Text.Json.Nodes.JsonObject { ["status"] = "failed", ["source"] = Node(await Identity()),
            ["ci"] = Node(CiIdentity()), ["started_at"] = Now(), ["runs"] = new System.Text.Json.Nodes.JsonArray() };
        try
        {
            for (int i = 0; i < 50; i++)
            {
                var path = Path.Combine(output, i.ToString("D2"));
                var run = await Execute(["dotnet", "test", Project, "-c", "Release", "-p:LibTfm=" + PrimaryTfm,
                    "--filter", "FullyQualifiedName~WebsocketClientLiteTest.PayloadLeaseTests|FullyQualifiedName=" + RuntimeTest,
                    "--logger", "trx;LogFileName=results.trx", "--results-directory", path], path, 180, cancellation);
                report["runs"]!.AsArray().Add(Node(run));
                Require(Passed(Node(run)), "Lease probe failed at repetition " + i);
                XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                var rows = XDocument.Load(Path.Combine(path, "results.trx")).Descendants(ns + "UnitTestResult").ToArray();
                Require(rows.Length == 15 && rows.All(r => (string?)r.Attribute("outcome") == "Passed"), "Incomplete lease probe cases");
                foreach (var (name, count) in LeaseCaseCounts)
                    Require(rows.Count(r => ((string?)r.Attribute("testName"))?.Contains("PayloadLeaseTests." + name, StringComparison.Ordinal) == true) == count, "Missing lease probe schedule");
                Console.WriteLine($"Lease probe {i + 1}/50 passed");
            }
            report["status"] = "passed";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { report["status"] = "failed"; report["error"] = e.Message; }
        finally { report["source_after"] = Node(await Identity()); report["finished_at"] = Now(); Save(Path.Combine(output, "results.json"), report); }
        return S(report, "status") == "passed" ? 0 : 1;
    }
}
