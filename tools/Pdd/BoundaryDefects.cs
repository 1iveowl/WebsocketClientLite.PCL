using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    // Exact source edits in isolated copies. Each must fail its own behavioral assertion.
    public static readonly (string Id, string Before, string After, string Test, string Assertion, int Cases)[] BoundaryCases =
    [
        ("read-failure", "sender.NotifyReadFailure();", ";", "ReadFailureReleasesReadinessBeforeObserverError", "Read failure did not release Close readiness", 1),
        ("completion-fallback", "await closeOnceAsync().ConfigureAwait(false);", ";", "ParserCompletionAwaitsFallbackCloseBeforeObserverCompletion", "Parser completion did not start fallback Close", 1),
        ("cancellation", "if (!ct.IsCancellationRequested)", "if (ct.IsCancellationRequested)", "CancelledParserLeavesCloseToDisposalAndDoesNotCompleteObserver", "Cancelled parser started fallback Close", 1),
        ("observer-error", "dataframeObserver.OnError(sourceError);", ";", "ObserverErrorPrecedesCloseAndNeverBecomesCompletion", "Observer error was not routed before Close", 1),
        ("observer-completion", "dataframeObserver.OnCompleted();", ";", "ParserCompletionRoutesOneObserverCompletionAfterClose", "Parser did not route exactly one observer completion", 1),
        ("async-ping", "// Stop the ping timer before saying goodbye.\n        _clientPingDisposable?.Dispose();", "// Stop the ping timer before saying goodbye.\n        ;", "DisposalStopsActivePeriodicPingsBeforeClose", "Periodic ping subscription remained active", 1),
        ("sync-ping", "// Stop the ping timer before saying goodbye.\n        _clientPingDisposable?.Dispose();", "// Stop the ping timer before saying goodbye.\n        ;", "DisposalStopsActivePeriodicPingsBeforeClose", "Periodic ping subscription remained active", 1),
        ("active-ping", "// Stop the ping timer before saying goodbye.\n        _clientPingDisposable?.Dispose();", "// Stop the ping timer before saying goodbye.\n        ;", "DisposalCancelsPausedPeriodicPingAndReleasesTransport", "Disposal did not cancel the active periodic Ping", 1),
        ("sync-context", "await closeOnce.Value.ConfigureAwait(false);", "await closeOnce.Value.ConfigureAwait(true);", "AsyncDisposalDoesNotRequireCallingSynchronizationContext", "Disposal captured the calling SynchronizationContext", 1)
    ];

    public async Task<int> BoundaryDefects(string output, CancellationToken cancellation)
    {
        Fresh(output);
        var report = new JsonObject { ["status"] = "failed", ["source"] = Node(await Identity()), ["ci"] = Node(CiIdentity()),
            ["started_at"] = Now(), ["library_target"] = "net10.0", ["experiments"] = new JsonArray() };
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
                ["dotnet", "test", Project, "-c", "Release", "-p:LibTfm=net10.0", "--filter", filter + "|FullyQualifiedName=" + RuntimeTest,
                    "--logger", "trx;LogFileName=results.trx", "--results-directory", Path.Combine(output, name)], Path.Combine(output, name), 180, cancellation);
            report["baseline"] = Node(await Run("baseline", "FullyQualifiedName~WebsocketClientLiteTest.CloseBoundaryTests"));
            Require(Passed(report["baseline"]), "Boundary baseline failed");
            var file = Path.Combine(copy.Path, CloseSource);
            var original = Text(file);
            foreach (var c in BoundaryCases)
            {
                // Five independent process runs for the previously unstable critical statement.
                for (int repeat = 0; repeat < (c.Id == "read-failure" ? 5 : 1); repeat++)
                {
                    var name = c.Id + "-" + repeat;
                    var index = c.Id == "sync-ping" ? original.LastIndexOf(c.Before, StringComparison.Ordinal) : original.IndexOf(c.Before, StringComparison.Ordinal);
                    Require(index >= 0 && (c.Id.EndsWith("ping", StringComparison.Ordinal) || original.IndexOf(c.Before, index + 1, StringComparison.Ordinal) < 0), "Ambiguous/missing boundary edit");
                    File.WriteAllText(file, original[..index] + c.After + original[(index + c.Before.Length)..]);
                    var run = await Run(name, "FullyQualifiedName=WebsocketClientLiteTest.CloseBoundaryTests." + c.Test);
                    var experiment = new JsonObject { ["id"] = name, ["status"] = "failed", ["test"] = c.Test, ["assertion"] = c.Assertion,
                        ["source_hash"] = Hash(System.Text.Encoding.UTF8.GetBytes(original)), ["mutant_hash"] = Hash(File.ReadAllBytes(file)),
                        ["before"] = c.Before, ["after"] = c.After, ["run"] = Node(run) };
                    report["experiments"]!.AsArray().Add(experiment);
                    experiment["host_runtime"] = ValidateBoundaryResult(Path.Combine(output, name, "results.trx"), Node(run), c.Test, c.Assertion, c.Cases);
                    experiment["status"] = "passed";
                    File.WriteAllText(file, original);
                }
            }
            report["status"] = "passed";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { report["error"] = e.Message; }
        finally { report["finished_at"] = Now(); report["source_after"] = Node(await Identity()); Save(Path.Combine(output, "results.json"), report); }
        return S(report, "status") == "passed" ? 0 : 1;
    }

    public static void ValidateBoundaryReport(string directory, JsonNode report, string sourceHash)
    {
        Require(S(report, "status") == "passed" && S(report, "library_target") == "net10.0" && Passed(report["baseline"]), "Incomplete boundary baseline/report");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        ValidateAssertionExecution(Path.Combine(directory, "baseline"), 0);
        var baseline = XDocument.Load(Path.Combine(directory, "baseline/results.trx")).Descendants(ns + "UnitTestResult").ToArray();
        Require(baseline.Length == 10 && baseline.All(r => (string?)r.Attribute("outcome") == "Passed"), "Incomplete boundary baseline cases");
        foreach (var test in BoundaryCases.Select(c => c.Test).Distinct())
            Require(baseline.Count(r => ((string?)r.Attribute("testName"))?.Contains("CloseBoundaryTests." + test, StringComparison.Ordinal) == true) == (test == "DisposalStopsActivePeriodicPingsBeforeClose" ? 2 : 1), "Missing named boundary baseline case");
        var baselineRuntime = baseline.Single(r => ((string?)r.Attribute("testName"))?.EndsWith("RecordHostRuntime", StringComparison.Ordinal) == true).Value;
        var entries = report["experiments"]!.AsArray();
        Require(entries.Count == 13, "Missing boundary experiments or repetitions");
        foreach (var c in BoundaryCases)
        {
            for (int repeat = 0; repeat < (c.Id == "read-failure" ? 5 : 1); repeat++)
            {
                var name = c.Id + "-" + repeat;
                var e = entries.Single(e => S(e, "id") == name)!;
                Require(S(e, "status") == "passed" && S(e, "source_hash") == sourceHash && S(e, "mutant_hash").Length == 64 && S(e, "mutant_hash") != sourceHash, "Boundary mutation identity mismatch");
                Require(S(e, "before") == c.Before && S(e, "after") == c.After && S(e, "test") == c.Test && S(e, "assertion") == c.Assertion, "Boundary experiment contract mismatch");
                var runtime = ValidateBoundaryResult(Path.Combine(directory, name, "results.trx"), e["run"]!, c.Test, c.Assertion, c.Cases);
                Require(S(e, "host_runtime") == runtime && baselineRuntime.Contains("PDD_HOST_RUNTIME=" + runtime, StringComparison.Ordinal), "Boundary runtime mismatch");
            }
        }
    }

    public static string ValidateBoundaryResult(string trxPath, JsonNode run, string test, string assertion, int cases)
    {
        Require(S(run, "status") == "failed" && run["exit_code"]?.ToString() == "1", "Boundary mutant must fail assertions, not timeout or compilation");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        ValidateAssertionExecution(Path.GetDirectoryName(trxPath)!, 1);
        var tree = XDocument.Load(trxPath);
        var results = tree.Descendants(ns + "UnitTestResult").ToArray();
        Require(results.Length == (test == "DisposalStopsActivePeriodicPingsBeforeClose" ? 3 : 2), "Missing boundary result rows");
        var failed = results.Where(r => (string?)r.Attribute("outcome") == "Failed").ToArray();
        Require(failed.Length == cases && failed.All(r => ((string?)r.Attribute("testName"))?.Contains(test, StringComparison.Ordinal) == true && r.Value.Contains(assertion, StringComparison.Ordinal)), "Missing intended boundary assertion");
        Require(results.All(r => (string?)r.Attribute("outcome") is "Passed" or "Failed"), "Incomplete boundary cases");
        var runtime = results.Single(r => ((string?)r.Attribute("testName"))?.EndsWith("RecordHostRuntime", StringComparison.Ordinal) == true);
        var match = System.Text.RegularExpressions.Regex.Match(runtime.Value, @"PDD_HOST_RUNTIME=(.+)");
        Require((string?)runtime.Attribute("outcome") == "Passed" && match.Success, "Missing boundary host runtime");
        return match.Groups[1].Value.Trim();
    }
}
