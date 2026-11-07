using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    public const string CriticalTest = "WebsocketClientLiteTest.ControlledCloseTests.PausedCloseWriteBlocksDisposalAndPeerRaceSendsOnce";
    public const string RuntimeTest = "WebsocketClientLiteTest.RuntimeEvidenceTests.RecordHostRuntime";
    public async Task<int> ControlledDefect(string output, CancellationToken cancellation)
    {
        Fresh(output);
        var source = await Identity();
        var copy = Path.Combine(Path.GetTempPath(), "pdd-ordering-" + Guid.NewGuid().ToString("N"));
        var report = new System.Text.Json.Nodes.JsonObject
        {
            ["status"] = "failed", ["source"] = Node(source), ["ci"] = Node(CiIdentity()),
            ["started_at"] = Now(), ["library_target"] = "net10.0", ["experiment"] = "dispose-before-close-completion"
        };
        try
        {
            // Copy only repository inputs. Never mutate the user's production checkout.
            var paths = (await ContractReport.Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard" )).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                if (!File.Exists(Path.Combine(root, path))) continue;
                var target = Path.Combine(copy, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(root, path), target);
            }
            var runner = new Evidence(copy);
            async Task<CommandReceipt> Run(string name) => await runner.Execute(
                ["dotnet", "test", Project, "-c", "Release", "-p:LibTfm=net10.0", "--filter", $"FullyQualifiedName~{CriticalTest}|FullyQualifiedName={RuntimeTest}", "--logger", "trx;LogFileName=results.trx", "--results-directory", Path.Combine(output, name)], Path.Combine(output, name), 180, cancellation);
            report["baseline"] = Node(await Run("baseline"));
            Require(Passed(report["baseline"]), "Ordering baseline did not pass");
            var file = Path.Combine(copy, "src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs");
            var original = Text(file);
            const string before = "await closeOnce.Value.ConfigureAwait(false);";
            const string after = "_ = closeOnce.Value; // isolated ordering defect";
            Require(original.Split(before).Length == 2, "Ordering mutation anchor is not unique");
            File.WriteAllText(file, original.Replace(before, after));
            report["mutation"] = Node(new { file = "src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs", before, after, original_hash = Hash(System.Text.Encoding.UTF8.GetBytes(original)), mutant_hash = Hash(File.ReadAllBytes(file)) });
            report["mutant"] = Node(await Run("mutant"));
            report["host_runtime"] = ValidateDefect(output, report["baseline"]!, report["mutant"]!);
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
    public static string ValidateDefect(string directory, System.Text.Json.Nodes.JsonNode baseline, System.Text.Json.Nodes.JsonNode mutant)
    {
        Require(Passed(baseline), "Ordering baseline failed");
        Require(S(mutant, "status") == "failed" && mutant["exit_code"]?.ToString() == "1", "Ordering mutant must fail assertions, not timeout or compilation");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        string? host = null;
        foreach (var phase in new[] { "baseline", "mutant" })
        {
            var tree = XDocument.Load(Path.Combine(directory, phase, "results.trx"));
            var definitions = tree.Descendants(ns + "UnitTest").ToDictionary(e => (string)e.Attribute("id")!, e => e.Element(ns + "TestMethod")!);
            var results = tree.Descendants(ns + "UnitTestResult").ToArray();
            var controlled = results.Where(r => (string?)definitions[(string)r.Attribute("testId")!].Attribute("name") == CriticalTest.Split('.').Last()).ToArray();
            Require(controlled.Length == 2, "Missing ordering schedules in TRX");
            Require(controlled.All(r => (string?)r.Attribute("outcome") == (phase == "baseline" ? "Passed" : "Failed")), "Unexpected ordering test result");
            if (phase == "mutant") Require(controlled.All(r => r.Value.Contains("Disposal overtook paused work")), "Failure was not the critical ordering assertion");
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
