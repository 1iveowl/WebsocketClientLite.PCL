using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    public const string RetentionSource = "src/main/WebsocketClientLite/Service/WebsocketParserHandler.cs";
    public const string HeaderSource = "src/main/WebsocketClientLite/Helper/DataframeParsing.cs";
    public static readonly Dictionary<string, int> RetentionCaseCounts = new()
    {
        ["SmallSequencesPreserveBytesMetadataAndReset"] = 1,
        ["AccumulatedLimitRejectsBeforePayloadRead"] = 2,
        ["ControlPayloadDoesNotChangeRetainedTotal"] = 1,
        ["ExtendedHeaderCapturesScratchBeforeReuse"] = 1,
        ["HugeLengthsRejectOnHeaderOnly"] = 3,
        ["IncompleteMessageTerminatesWithoutEmission"] = 2,
        ["CloseAndInvalidContinuationDoNotLeakRetainedData"] = 1,
        ["SplitUtf8IsValidatedOnlyWhenComplete"] = 1,
        ["HandshakeConsumesScratchImmediatelyAndStopsBeforeFrame"] = 1
    };
    public static readonly (string Id, string Source, string Before, string After, string Test, string Assertion)[] RetentionCases =
    [
        ("control-total", RetentionSource, "total = size;\n                        obs.OnNext(frame);",
            "total = size + (uint)payload.Length;\n                        obs.OnNext(frame);",
            "ControlPayloadDoesNotChangeRetainedTotal", "Control payload changed retained budget"),
        ("late-scratch", HeaderSource, "Protocol.Admit(b0, b1, ext, (uint)count, state, total, (uint)tcpConnection.MaxFrameSize,",
            "Protocol.Admit(header[0], header[1], ext, (uint)count, state, total, (uint)tcpConnection.MaxFrameSize,",
            "ExtendedHeaderCapturesScratchBeforeReuse", "Header scratch capture corrupted admission")
    ];
    public async Task<int> RetentionDefects(string output, CancellationToken cancellation)
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
            report["baseline"] = Node(await Run("baseline", "FullyQualifiedName~WebsocketClientLiteTest.ParserRetentionTests"));
            Require(Passed(report["baseline"]), "Retention baseline failed");
            foreach (var c in RetentionCases)
            {
                var file = Path.Combine(copy.Path, c.Source);
                var original = Text(file);
                var name = c.Id;
                var index = original.IndexOf(c.Before, StringComparison.Ordinal);
                Require(index >= 0 && original.IndexOf(c.Before, index + 1, StringComparison.Ordinal) < 0, "Ambiguous/missing retention edit");
                File.WriteAllText(file, original[..index] + c.After + original[(index + c.Before.Length)..]);
                var run = await Run(name, "FullyQualifiedName=WebsocketClientLiteTest.ParserRetentionTests." + c.Test);
                var experiment = new JsonObject { ["id"] = name, ["status"] = "failed", ["test"] = c.Test, ["assertion"] = c.Assertion,
                    ["source_hash"] = Hash(System.Text.Encoding.UTF8.GetBytes(original)), ["mutant_hash"] = Hash(File.ReadAllBytes(file)),
                    ["before"] = c.Before, ["after"] = c.After, ["run"] = Node(run) };
                report["experiments"]!.AsArray().Add(experiment);
                experiment["host_runtime"] = ValidateRetentionResult(Path.Combine(output, name), Node(run), c.Test, c.Assertion);
                experiment["status"] = "passed";
                File.WriteAllText(file, original);
            }
            report["status"] = "passed";
            ValidateRetentionReport(output, report, (await Identity()).SourceHashes);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { report["status"] = "failed"; report["error"] = e.Message; }
        finally { report["finished_at"] = Now(); report["source_after"] = Node(await Identity()); Save(Path.Combine(output, "results.json"), report); }
        return S(report, "status") == "passed" ? 0 : 1;
    }

    public static void ValidateRetentionReport(string directory, JsonNode report, IReadOnlyDictionary<string, string?> sourceHashes)
    {
        Require(S(report, "status") == "passed" && S(report, "library_target") == "net10.0" && Passed(report["baseline"]), "Incomplete retention baseline/report");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var baseline = XDocument.Load(Path.Combine(directory, "baseline/results.trx")).Descendants(ns + "UnitTestResult").ToArray();
        Require(baseline.Length == 14 && baseline.All(r => (string?)r.Attribute("outcome") == "Passed"), "Incomplete retention baseline cases");
        foreach (var (test, count) in RetentionCaseCounts)
            Require(baseline.Count(r => ((string?)r.Attribute("testName"))?.Contains("ParserRetentionTests." + test, StringComparison.Ordinal) == true) == count, "Missing named retention baseline case");
        ValidateRetentionExecution(Path.Combine(directory, "baseline"), 0);
        RequireRetentionCommand(report["baseline"]!);
        var baselineRuntime = baseline.Single(r => ((string?)r.Attribute("testName"))?.EndsWith("RecordHostRuntime", StringComparison.Ordinal) == true).Value;
        var entries = report["experiments"]!.AsArray();
        Require(entries.Count == 2, "Missing retention experiments or repetitions");
        foreach (var c in RetentionCases)
        {
            var name = c.Id;
            var e = entries.Single(e => S(e, "id") == name)!;
            Require(S(e, "status") == "passed" && S(e, "source_hash") == sourceHashes[c.Source] && S(e, "mutant_hash").Length == 64 && S(e, "mutant_hash") != sourceHashes[c.Source], "Retention mutation identity mismatch");
            Require(S(e, "before") == c.Before && S(e, "after") == c.After && S(e, "test") == c.Test && S(e, "assertion") == c.Assertion, "Retention experiment contract mismatch");
            var runtime = ValidateRetentionResult(Path.Combine(directory, name), e["run"]!, c.Test, c.Assertion);
            Require(S(e, "host_runtime") == runtime && baselineRuntime.Contains("PDD_HOST_RUNTIME=" + runtime, StringComparison.Ordinal), "Retention runtime mismatch");
        }
    }

    private static void RequireRetentionCommand(JsonNode run)
    {
        var command = run["command"]!.AsArray().Select(x => x!.ToString()).ToArray();
        int config = Array.IndexOf(command, "-c");
        Require(config >= 0 && config + 1 < command.Length && command[config + 1] == "Release" && command.Contains("-p:LibTfm=net10.0"), "Retention must use Release/net10.0");
    }
    private static void ValidateRetentionExecution(string directory, int code)
    {
        var e = Read(Path.Combine(directory, "execution.json"));
        Require(e["timed_out"]?.GetValue<bool>() == false && e["interrupted"]?.GetValue<bool>() == false && e["launch_error"] is null && e["exit_code"]?.ToString() == code.ToString(), "Invalid retention execution");
    }
    private static string ValidateRetentionResult(string directory, JsonNode run, string test, string assertion)
    {
        RequireRetentionCommand(run);
        ValidateRetentionExecution(directory, 1);
        return ValidateBoundaryResult(Path.Combine(directory, "results.trx"), run, test, assertion, 1);
    }
}
