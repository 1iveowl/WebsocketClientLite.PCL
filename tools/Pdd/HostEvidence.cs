using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Pdd.Data;

namespace Pdd;

// A cell is a test host plus library build, never a LibTfm alias for a runtime.
public record HostCell(string Id, string HostTfm, string LibraryTfm, string Os, string Architecture, string Placement);

public sealed partial class Evidence
{
    public static readonly HostCell[] HostCells =
    [
        .. Tfms.Select(t => new HostCell("linux-10-" + t, "net10.0", t, "linux", "X64", "mandatory")),
        .. new[] { "net10.0", "netstandard2.0" }.Select(t => new HostCell("windows-10-" + t, "net10.0", t, "windows", "X64", "manual-not-qualified")),
        .. new[] { "net10.0", "netstandard2.0" }.Select(t => new HostCell("macos-10-" + t, "net10.0", t, "macos", "Arm64", "scheduled-not-qualified")),
        new("linux-8-net8.0", "net8.0", "net8.0", "linux", "X64", "manual-transitional"),
        new("linux-9-net9.0", "net9.0", "net9.0", "linux", "X64", "manual-transitional")
    ];
    public static HostCell FindHostCell(string id) => HostCells.SingleOrDefault(c => c.Id == id) ?? throw new ArgumentException("Unknown host cell: " + id);
    public static string FrameworkName(string tfm) => (tfm.StartsWith("netstandard", StringComparison.Ordinal) ? ".NETStandard,Version=v" + tfm[11..] : ".NETCoreApp,Version=v" + tfm[3..]);

    public static void ValidateHostIdentity(HostCell cell, JsonNode? host, string runtime, string? expectedHash = null)
    {
        Require(host is JsonObject, "missing actual host identity");
        var major = cell.HostTfm[3..].Split('.')[0];
        Require(Regex.IsMatch(S(host, "runtime_version"), "^" + major + @"\.0\.\d+$") && S(host, "runtime") == runtime && runtime == ".NET " + S(host, "runtime_version"), "wrong host runtime/patch");
        Require(S(host, "test_framework") == FrameworkName(cell.HostTfm), "wrong test host target");
        Require(S(host, "os") == cell.Os && S(host, "process_architecture") == cell.Architecture && S(host, "os_architecture") == cell.Architecture, "wrong OS/architecture");
        Require(S(host, "os_version").Length > 0 && S(host, "os_description").Length > 0, "missing OS version");
        Require(cell.Os switch
        {
            "linux" => Regex.IsMatch(S(host, "os_release"), "(?m)^ID=ubuntu\\r?$") && Regex.IsMatch(S(host, "os_release"), "(?m)^VERSION_ID=\"24\\.04\"\\r?$"),
            "windows" => S(host, "os_version").Contains("10.0.20348", StringComparison.Ordinal),
            "macos" => Regex.IsMatch(S(host, "os_description"), @"Darwin .*\b24\."),
            _ => false
        }, "wrong OS release (requires Ubuntu 24.04 / Server 2022 / macOS 15)");
        Require(S(host, "library_framework") == FrameworkName(cell.LibraryTfm), "wrong loaded library target");
        Require(S(host, "library_name").StartsWith("WebsocketClientLite, Version=", StringComparison.Ordinal) && S(host, "library_path").Replace('\\', '/').EndsWith("/WebsocketClientLite.dll", StringComparison.Ordinal) && Guid.TryParse(S(host, "library_mvid"), out _), "missing loaded library identity");
        Require(Regex.IsMatch(S(host, "library_sha256"), "^[a-f0-9]{64}$") && (expectedHash is null || S(host, "library_sha256") == expectedHash), "wrong loaded library assembly hash");
    }

    public async Task<int> TestHost(string id, string output, double budget, CancellationToken cancellation)
    {
        var cell = FindHostCell(id);
        Fresh(output);
        var report = new JobReceipt { Job = id, Source = await Identity() };
        report.Tools["cell"] = Node(cell);
        report.Tools["runner"] = Node(new[] { "RUNNER_OS", "RUNNER_ARCH", "ImageOS", "ImageVersion", "RUNNER_NAME" }.ToDictionary(k => k, Environment.GetEnvironmentVariable));
        void SaveReport() => Save(Path.Combine(output, "job.json"), report);
        SaveReport();
        var properties = new[] { "-p:TestHostTfm=" + cell.HostTfm, "-p:LibTfm=" + cell.LibraryTfm };
        var target = Path.Combine(output, "tests", cell.LibraryTfm);
        var commands = new (string, string[])[]
        {
            ("sdk", ["dotnet", "--info"]),
            ("restore", ["dotnet", "restore", Project, .. properties]),
            ("build", ["dotnet", "build", Project, "-c", "Release", "--no-restore", .. properties]),
            ("test", ["dotnet", "test", Project, "-c", "Release", "--no-build", "--no-restore", .. properties, "--logger", "trx;LogFileName=results.trx", "--results-directory", target])
        };
        var success = true;
        try
        {
            foreach (var (name, command) in commands)
            {
                report.Runs[name] = success ? await Execute(command, name == "test" ? target : Path.Combine(output, name), budget, cancellation) : new CommandReceipt { Command = command, Status = "skipped" };
                success &= report.Runs[name].Status == "passed";
                SaveReport();
                if (name == "build" && success)
                {
                    var library = Path.Combine(root, "src/main/WebsocketClientLite/bin/Release", cell.LibraryTfm, "WebsocketClientLite.dll");
                    File.Copy(library, Path.Combine(output, "expected-library.dll"));
                }
                Console.WriteLine($"{id}/{name}: {report.Runs[name].Status}");
            }
        }
        finally { report.SourceAfter = await Identity(); report.FinishedAt = Now(); SaveReport(); }
        try { ValidateHostCell(output, cell, report.Source, Node(CiIdentity())); }
        catch (Exception e) when (e is not OutOfMemoryException) { Console.Error.WriteLine(id + ": " + e.Message); return 1; }
        return success ? 0 : 1;
    }

    public static JsonNode ValidateHostCell(string directory, HostCell cell, SourceIdentity expected, JsonNode ci)
    {
        var report = Read(Path.Combine(directory, "job.json"));
        Require(!expected.DirtyWorktree && Equal(report["source"], Node(expected)) && Equal(report["source_after"], Node(expected)), "source mismatch or dirty/incomplete qualification");
        Require(S(report, "job") == cell.Id && Equal(report["tools"]?["cell"], Node(cell)), "wrong cell identity");
        foreach (var key in new[] { "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_REPOSITORY", "GITHUB_EVENT_NAME", "GITHUB_SHA" })
            Require(Equal(report["ci"]?[key], ci[key]), "source run/attempt mismatch: " + key);
        foreach (var name in new[] { "sdk", "restore", "build", "test" })
            {
            Require(Passed(report["runs"]?[name]), name + " did not pass (compatibility/infrastructure is not a library assertion failure)");
            var commandDirectory = name == "test" ? Path.Combine(directory, "tests", cell.LibraryTfm) : Path.Combine(directory, name);
            var execution = Model<ExecutionResult>(Read(Path.Combine(commandDirectory, "execution.json")));
            Require(execution.Success && execution.ElapsedSeconds >= 0 && double.IsFinite(execution.ElapsedSeconds), name + " execution failed/timed out/interrupted");
            Require(Equal(Node(execution.Command), report["runs"]![name]!["command"]), name + " command mismatch");
        }
        var matrix = TestResults(directory);
        var result = matrix[cell.LibraryTfm]!;
        Require(S(result, "status") == "passed", "missing or failed test results");
        var command = result["execution"]!["command"]!.AsArray().Select(x => x!.ToString()).ToArray();
        Require(command.Contains("-p:TestHostTfm=" + cell.HostTfm) && command.Contains("Release") && !command.Contains("--filter"), "wrong host command/configuration/scope");
        Require(Equal(result["execution"], report["runs"]!["test"]), "test execution mismatch");
        Require(result["case_counts"]!.AsObject().Sum(p => p.Value!.GetValue<int>()) >= 269, "incomplete full suite");
        foreach (var (prefix, cases) in new[] { ("QueuedWriteTests.", QueuedCaseCounts), ("PayloadLeaseTests.", LeaseCaseCounts), ("ParserRetentionTests.", RetentionCaseCounts), ("ReconnectTests.", ReconnectCaseCounts) })
            foreach (var (name, count) in cases)
                Require(result["case_counts"]?["WebsocketClientLiteTest." + prefix + name]?.GetValue<int>() == count, "missing boundary case: " + prefix + name);
        ValidateHostIdentity(cell, result["host"], S(result, "host_runtime"), Hash(File.ReadAllBytes(Path.Combine(directory, "expected-library.dll"))));
        return result.DeepClone();
    }

    // Shared with full aggregation; manual qualification never satisfies proof/mutation gates.
    public static JsonObject HostResults(string directory, SourceIdentity source, JsonNode ci, IEnumerable<HostCell> required)
    {
        var cells = new JsonObject();
        var errors = new JsonArray();
        foreach (var cell in required)
        {
            if (cells.ContainsKey(cell.Id)) { errors.Add(cell.Id + ": duplicate cell"); continue; }
            try { cells[cell.Id] = ValidateHostCell(Path.Combine(directory, cell.Id), cell, source, ci); }
            catch (Exception e) when (e is not OutOfMemoryException) { errors.Add(cell.Id + ": " + e.Message); cells[cell.Id] = new JsonObject { ["status"] = "failed" }; }
        }
        return new JsonObject { ["source"] = Node(source), ["ci"] = ci.DeepClone(), ["cells"] = cells, ["errors"] = errors, ["gate"] = new JsonObject { ["status"] = errors.Count == 0 && cells.Count > 0 ? "passed" : "failed" } };
    }
}
