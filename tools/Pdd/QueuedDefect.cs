using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    public static readonly Dictionary<string, int> QueuedCaseCounts = new()
    {
        ["AbortDrainsNonCancelableFrameWaiterAfterOwnerSettles"] = 1,
        ["CancellationBeforeOrWhileQueuedPreservesAdmission"] = 4,
        ["PreCancelledControlNeverAcquiresFrameOwnership"] = 2,
        ["FrameSerializationAdmitsTwoDataSendsAndOneControlWithoutOverlap"] = 1,
        ["TerminalNotificationRejectsDataWaitingAtEitherGate"] = 4,
        ["AbortAllowsQueuedCancellationWhileOwnerRemainsInFlight"] = 1
    };
    public const string QueuedTest = "WebsocketClientLiteTest.QueuedWriteTests.FrameSerializationAdmitsTwoDataSendsAndOneControlWithoutOverlap";
    public const string QueuedSource = "src/main/WebsocketClientLite/Service/TcpConnectionService.cs";
    public const string QueuedBefore = "private readonly SemaphoreSlim _writeGate = new(1, 1);";
    public const string QueuedAfter = "private readonly SemaphoreSlim _writeGate = new(2, 2);";
    public async Task<int> QueuedDefect(string output, CancellationToken cancellation)
    {
        Fresh(output);
        var source = await Identity();
        var copy = Path.Combine(Path.GetTempPath(), "pdd-queued-" + Guid.NewGuid().ToString("N"));
        var report = new System.Text.Json.Nodes.JsonObject
        {
            ["status"] = "failed", ["source"] = Node(source), ["ci"] = Node(CiIdentity()),
            ["started_at"] = Now(), ["library_target"] = PrimaryTfm, ["experiment"] = "frame-overlap"
        };
        try
        {
            // Copy only repository inputs. Never mutate the user's production checkout.
            var paths = (await ContractReport.Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "src", "verification", "tools", "scripts", ".config", "global.json", "Directory.Build.props", "Directory.Build.targets", "NuGet.config" )).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths.Distinct())
            {
                if (!File.Exists(Path.Combine(root, path))) continue;
                var target = Path.Combine(copy, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(root, path), target);
            }
            var runner = new Evidence(copy);
            async Task<CommandReceipt> Run(string name) => await runner.Execute(
                ["dotnet", "test", Project, "-c", "Release", "-p:LibTfm=" + PrimaryTfm, "--filter", $"FullyQualifiedName~{QueuedTest}|FullyQualifiedName={RuntimeTest}", "--logger", "trx;LogFileName=results.trx", "--results-directory", Path.Combine(output, name)], Path.Combine(output, name), 180, cancellation);
            report["baseline"] = Node(await Run("baseline"));
            Require(Passed(report["baseline"]), "Queued baseline did not pass");
            var file = Path.Combine(copy, QueuedSource);
            var original = Text(file);
            const string before = QueuedBefore;
            const string after = QueuedAfter;
            Require(original.Split(before).Length == 2, "Queued mutation anchor is not unique");
            File.WriteAllText(file, original.Replace(before, after));
            report["mutation"] = Node(new { file = QueuedSource, before, after, original_hash = Hash(System.Text.Encoding.UTF8.GetBytes(original)), mutant_hash = Hash(File.ReadAllBytes(file)) });
            report["mutant"] = Node(await Run("mutant"));
            report["host_runtime"] = ValidateQueuedDefect(output, report["baseline"]!, report["mutant"]!);
            report["status"] = "passed";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { report["error"] = e.Message; }
        finally
        {
            report["finished_at"] = Now(); report["source_after"] = Node(await Identity());
            Save(Path.Combine(output, "results.json"), report);
            if (Directory.Exists(copy)) Directory.Delete(copy, true);
        }
        return S(report, "status") == "passed" ? 0 : 1;
    }
    // Manual smoke probe; deliberately not repeated fifty times in every PR job.
    public async Task<int> QueuedProbe(string output, CancellationToken cancellation)
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
                    "--filter", "FullyQualifiedName~WebsocketClientLiteTest.QueuedWriteTests|FullyQualifiedName=WebsocketClientLiteTest.ControlledCloseTests.CancellationBetweenFragmentsAllowsOnlyClose|FullyQualifiedName=" + RuntimeTest,
                    "--logger", "trx;LogFileName=results.trx", "--results-directory", path], path, 180, cancellation);
                report["runs"]!.AsArray().Add(Node(run));
                Require(Passed(Node(run)), "Queued probe failed at repetition " + i);
                XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                var rows = XDocument.Load(Path.Combine(path, "results.trx")).Descendants(ns + "UnitTestResult").ToArray();
                Require(rows.Length == 15 && rows.All(r => (string?)r.Attribute("outcome") == "Passed"), "Incomplete queued probe cases");
                foreach (var (name, count) in QueuedCaseCounts)
                    Require(rows.Count(r => ((string?)r.Attribute("testName"))?.Contains("QueuedWriteTests." + name, StringComparison.Ordinal) == true) == count, "Missing queued probe schedule");
                Console.WriteLine($"Queued probe {i + 1}/50 passed");
            }
            report["status"] = "passed";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { report["error"] = e.Message; }
        finally { report["source_after"] = Node(await Identity()); report["finished_at"] = Now(); Save(Path.Combine(output, "results.json"), report); }
        return S(report, "status") == "passed" ? 0 : 1;
    }
    public static void ValidateQueuedReport(string directory, System.Text.Json.Nodes.JsonNode report, string sourceHash)
    {
        Require(S(report, "status") == "passed" && S(report, "library_target") == PrimaryTfm, "Incomplete queued experiment");
        var edit = report["mutation"]!;
        Require(S(edit, "file") == QueuedSource && S(edit, "before") == QueuedBefore && S(edit, "after") == QueuedAfter &&
            S(edit, "original_hash") == sourceHash && S(edit, "mutant_hash").Length == 64 && S(edit, "mutant_hash") != sourceHash,
            "Queued mutation identity mismatch");
        var runtime = ValidateQueuedDefect(directory, report["baseline"]!, report["mutant"]!);
        Require(S(report, "host_runtime") == runtime, "Queued runtime mismatch");
    }
    public static string ValidateQueuedDefect(string directory, System.Text.Json.Nodes.JsonNode baseline, System.Text.Json.Nodes.JsonNode mutant)
    {
        Require(Passed(baseline), "Queued baseline failed");
        Require(S(mutant, "status") == "failed" && mutant["exit_code"]?.ToString() == "1", "Queued mutant must fail assertions, not timeout or compilation");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        string? host = null;
        foreach (var phase in new[] { "baseline", "mutant" })
        {
            var execution = Read(Path.Combine(directory, phase, "execution.json"));
            Require(execution["timed_out"]?.GetValue<bool>() == false && execution["interrupted"]?.GetValue<bool>() == false && execution["launch_error"] is null &&
                execution["exit_code"]?.ToString() == (phase == "baseline" ? "0" : "1"), "Queued execution was interrupted, timed out or failed to launch");
            var tree = XDocument.Load(Path.Combine(directory, phase, "results.trx"));
            var definitions = tree.Descendants(ns + "UnitTest").ToDictionary(e => (string)e.Attribute("id")!, e => e.Element(ns + "TestMethod")!);
            var results = tree.Descendants(ns + "UnitTestResult").ToArray();
            Require(results.Length == 2, "Unexpected queued result rows");
            var controlled = results.Where(r => (string?)definitions[(string)r.Attribute("testId")!].Attribute("name") == QueuedTest.Split('.').Last()).ToArray();
            Require(controlled.Length == 1, "Missing queued schedules in TRX");
            Require(controlled.All(r => (string?)r.Attribute("outcome") == (phase == "baseline" ? "Passed" : "Failed")), "Unexpected queued test result");
            if (phase == "mutant") Require(controlled.All(r => r.Value.Contains("Overlapping frame delegates")), "Failure was not the critical queued assertion");
            var runtime = results.Single(r => (string?)definitions[(string)r.Attribute("testId")!].Attribute("name") == "RecordHostRuntime");
            Require((string?)runtime.Attribute("outcome") == "Passed", "Missing runtime probe");
            var match = System.Text.RegularExpressions.Regex.Match(runtime.Value, @"PDD_HOST_RUNTIME=(.+)");
            Require(match.Success, "Missing actual host runtime");
            if (host is not null) Require(host == match.Groups[1].Value.Trim(), "Defect host mismatch");
            host = match.Groups[1].Value.Trim();
        }
        return host!;
    }
}
