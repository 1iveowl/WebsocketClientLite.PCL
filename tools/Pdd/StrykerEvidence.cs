using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    public const string CloseSource = "src/main/WebsocketClientLite/Service/WebsocketConnectionHandler.cs";
    public static string MutantKey(JsonNode mutant) => Hash(Encoding.UTF8.GetBytes(
        mutant["mutatorName"] + "|" + mutant["location"]!.ToJsonString() + "|" + mutant["replacement"]));

    public static JsonObject TriageStryker(JsonNode report, JsonNode dispositions, string sourceHash)
    {
        var files = report["files"]!.AsObject();
        var selected = files.Where(p => p.Key.Replace('\\', '/').EndsWith("Service/WebsocketConnectionHandler.cs", StringComparison.Ordinal)).ToArray();
        Require(selected.Length == 1, "Unexpected Stryker mutation scope");
        var file = selected.Single();
        foreach (var other in files.Where(p => p.Key != file.Key))
            Require(other.Value!["mutants"]!.AsArray().All(m => S(m, "status") is "Ignored" or "CompileError"), "Runnable mutant outside configured scope");
        Require(file.Key.Replace('\\', '/').EndsWith("Service/WebsocketConnectionHandler.cs", StringComparison.Ordinal), "Unexpected Stryker source file");
        Require(Hash(Encoding.UTF8.GetBytes(S(file.Value, "source"))) == sourceHash, "Stryker source text hash mismatch");
        Require(S(dispositions, "source_hash") == sourceHash, "Stale Stryker dispositions");
        var entries = dispositions["mutants"]!.AsArray().ToDictionary(d => S(d, "key"));
        var counts = new Dictionary<string, int> { ["Killed"] = 0, ["Survived"] = 0, ["NoCoverage"] = 0, ["Timeout"] = 0, ["CompileError"] = 0, ["Ignored"] = 0 };
        var triage = new JsonArray();
        var mutants = file.Value!["mutants"]!.AsArray();
        Require(mutants.Count > 0, "Empty Stryker report");
        var ids = new HashSet<string>();
        foreach (var m in mutants)
        {
            Require(ids.Add(S(m, "id")), "Duplicate Stryker mutant");
            var status = S(m, "status");
            Require(counts.ContainsKey(status), "Incomplete/unknown Stryker outcome: " + status);
            counts[status]++;
            if (status is "Killed" or "CompileError") continue;
            if (status == "Ignored")
            {
                Require(S(m, "statusReason") == "Removed by block already covered filter", "Unexpected ignored selected mutant");
                continue;
            }
            var key = MutantKey(m!);
            Require(entries.TryGetValue(key, out var disposition), "Untriaged Stryker outcome: " + S(m, "id") + " " + status + " " + key);
            Require(S(disposition, "reason").Length > 20 && S(disposition, "classification") is "equivalent" or "out-of-scope" or "accepted-gap", "Invalid disposition");
            // Outcomes remain raw: a timeout is never converted into a killed mutant.
            triage.Add(new JsonObject { ["id"] = S(m, "id"), ["outcome"] = status, ["disposition"] = disposition!.DeepClone() });
        }
        Require(counts["Killed"] > 0, "No behavioral mutation detection");
        foreach (var key in dispositions["required_killed"]?.AsArray() ?? [])
            Require(mutants.Any(m => MutantKey(m!) == key!.ToString() && S(m, "status") == "Killed"), "Critical Stryker experiment was not killed: " + key);
        var allCounts = files.SelectMany(f => f.Value!["mutants"]!.AsArray()).GroupBy(m => S(m, "status")).ToDictionary(g => g.Key, g => g.Count());
        return new JsonObject { ["status"] = "passed", ["counts"] = Node(counts), ["all_reported_counts"] = Node(allCounts), ["dispositions"] = triage,
            ["requirements"] = Node(new[] { "CLOSE-004", "CLOSE-005" }),
            ["scope"] = "Connection handler only; accepted gaps are not detected defects. Critical ordering experiment gates separately." };
    }

    JsonObject StrykerEvidence(string directory, SourceIdentity expected)
    {
        var reports = Directory.GetFiles(Path.Combine(directory, "reports"), "mutation-report.json", SearchOption.AllDirectories);
        Require(reports.Length == 1, "Missing or ambiguous Stryker JSON report");
        var raw = Read(reports[0]);
        var result = TriageStryker(raw, Read(Path.Combine(root, "verification/stryker-close-dispositions.json")), expected.SourceHashes[CloseSource]!);
        var log = Text(Path.Combine(directory, "stryker.log"));
        Require(log.Contains("Version: 5.0.0"), "Missing pinned Stryker tool version");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var trx = XDocument.Load(Path.Combine(directory, "runtime/runtime.trx"));
        var runtimeResult = trx.Descendants(ns + "UnitTestResult").Single();
        Require((string?)runtimeResult.Attribute("outcome") == "Passed", "Runtime probe failed");
        var runtime = System.Text.RegularExpressions.Regex.Match(runtimeResult.Value, @"PDD_HOST_RUNTIME=(.+)");
        Require(runtime.Success, "Missing Stryker test host runtime");
        result["host_runtime"] = runtime.Groups[1].Value.Trim();
        result["library_target"] = "net10.0";
        result["tool_version"] = "5.0.0";
        result["config_sha256"] = expected.SourceHashes["src/main/WebsocketClientLiteTest/stryker-config.json"];
        var execution = Read(Path.Combine(directory, "commands/stryker/execution.json"));
        Require(execution["exit_code"]?.ToString() == "0" && execution["timed_out"]?.GetValue<bool>() == false && execution["interrupted"]?.GetValue<bool>() == false && execution["launch_error"] is null, "Stryker execution was incomplete");
        result["elapsed_seconds"] = execution["elapsed_seconds"]?.DeepClone();
        result["report"] = Path.GetRelativePath(directory, reports[0]);
        result["report_sha256"] = Hash(File.ReadAllBytes(reports[0]));
        return result;
    }
}
