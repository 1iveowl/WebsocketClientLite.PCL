using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence(string root)
{
    public static readonly string[] Tfms = ["netstandard2.0", "netstandard2.1", "net8.0", "net9.0", "net10.0"];
    public const string Project = "src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj";
    public static Dictionary<string, string?> CiIdentity() => new[] { "GITHUB_SERVER_URL", "GITHUB_REPOSITORY", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_EVENT_NAME", "GITHUB_JOB", "GITHUB_SHA" }.ToDictionary(k => k, Environment.GetEnvironmentVariable);
    public async Task<SourceIdentity> Identity()
    {
        async Task<string> Git(params string[] args) => (await ContractReport.Git(root, args)).TrimEnd('\n');
        var paths = (await Git("ls-files", "-z", "--cached", "--others", "--exclude-standard", "src", "verification", "scripts", "tools", ".github", ".config", "global.json", "Directory.Build.props", "Directory.Build.targets", "NuGet.config", "GitVersion.yml", "test-all-tfms.sh")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var hashes = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var p in paths.Distinct())
            hashes[p] = File.Exists(Path.Combine(root, p)) ? Hash(File.ReadAllBytes(Path.Combine(root, p))) : null;
        return new(await Git("rev-parse", "HEAD"), Environment.GetEnvironmentVariable("PDD_PR_HEAD") is { Length: > 0 } head ? head : null, (await Git("status", "--porcelain")).Length > 0, hashes);
    }
    public static List<(string Name, string[] Command)> Commands(string job, string output) => job == "stryker" ?
        [("runtime", ["dotnet", "test", Project, "-c", "Release", "-p:LibTfm=net10.0", "--filter", "FullyQualifiedName=" + RuntimeTest, "--logger", "trx;LogFileName=runtime.trx", "--results-directory", Path.Combine(output, "runtime")]), ("stryker", ["./scripts/stryker-close.sh"])] : job == "controlled" ?
        [("defect", ["./scripts/pdd.sh", "controlled-defect", "--output", Path.Combine(output, "defect")])] : job == "protocol" ?
        [("manifest", ["./scripts/pdd.sh", "validate"]), ("fixtures", ["dotnet", "test", "tools/Pdd.Tests/Pdd.Tests.csproj", "-c", "Release", "--nologo"]), ("proof", ["./scripts/dafny-masking.sh", "--check"]), ("mutations", ["./scripts/pdd.sh", "mutations", "--output", Path.Combine(output, "mutations")])] :
        [("restore", ["dotnet", "restore", "src/main/WebsocketClientLite.sln"]), ("build", ["dotnet", "build", "src/main/WebsocketClientLite.sln", "--configuration", "Release", "--no-restore"]), ("matrix", ["./test-all-tfms.sh", "Release"]), ("coverage-run", ["dotnet", "test", Project, "--configuration", "Release", "--collect:XPlat Code Coverage", "--results-directory", Path.Combine(output, "coverage")]), ("coverage-gate", ["./scripts/check-coverage.sh", Path.Combine(output, "coverage"), "70"])];
    public async Task<CommandReceipt> Execute(string[] command, string output, double budget = 1800, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(output);
        var run = new CommandReceipt { Command = command, StartedAt = Now() };
        Save(Path.Combine(output, "run.json"), run);
        var result = await Processes.Run(command, root, output, budget, cancellation, combinedLog: Path.Combine(output, "output.log"));
        run.ExitCode = result.ExitCode;
        run.Status = result.Success ? "passed" : "failed";
        run.FinishedAt = Now();
        Save(Path.Combine(output, "execution.json"), result);
        Save(Path.Combine(output, "run.json"), run);
        return run;
    }
    public async Task<int> TestTarget(string tfm, string output, double budget, CancellationToken cancellation)
    {
        Require(Tfms.Contains(tfm), "unsupported library target");
        Fresh(output);
        var result = await Execute(["dotnet", "test", Project, "-c", Environment.GetEnvironmentVariable("PDD_TEST_CONFIG") ?? "Release", "-p:LibTfm=" + tfm, "--nologo", "--logger", "trx;LogFileName=results.trx", "--results-directory", output], output, budget, cancellation);
        Console.Write(File.ReadAllText(Path.Combine(output, "output.log")));
        return result.Status == "passed" ? 0 : 1;
    }
    public async Task<int> Capture(string job, string output, double budget, CancellationToken cancellation = default, List<(string Name, string[] Command)>? commands = null)
    {
        Require(job is "protocol" or "tests" or "controlled" or "stryker", "unsupported job");
        Fresh(output);
        commands ??= Commands(job, output);
        var report = new JobReceipt { Job = job, Source = await Identity() };
        report.Tools["pdd"] = Node(new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            version = typeof(Evidence).Assembly.GetName().Version?.ToString()
        });
        foreach (var (name, command) in commands)
            report.Runs[name] = new CommandReceipt { Command = command, Log = name + ".log" };
        void SaveJob() => Save(Path.Combine(output, "job.json"), report);
        SaveJob();
        var failed = false;
        try
        {
            var version = await Processes.Run(["dotnet", "--info"], root, Path.Combine(output, "dotnet"), 60, cancellation);
            report.Tools["dotnet"] = Node(new
            {
                command = version.Command,
                exit_code = version.ExitCode,
                output = version.Stdout + version.Stderr
            });
            foreach (var (name, command) in commands)
            {
                var run = report.Runs[name];
                if (failed || cancellation.IsCancellationRequested)
                {
                    run.Status = "skipped";
                    failed = true;
                }
                else
                {
                    run.StartedAt = Now();
                    SaveJob();
                    var result = await Processes.Run(command, root, Path.Combine(output, "commands", name), budget, cancellation, new Dictionary<string, string> { ["PDD_TEST_RESULTS"] = Path.Combine(output, "tests"), ["PDD_STRYKER_OUTPUT"] = Path.Combine(output, "reports") }, Path.Combine(output, run.Log));
                    Save(Path.Combine(output, "commands", name, "execution.json"), result);
                    run.ExitCode = result.ExitCode;
                    run.Status = result.Success ? "passed" : "failed";
                    run.FinishedAt = Now();
                    failed = run.Status != "passed";
                    Console.WriteLine($"{name}: {run.Status}");
                }
                SaveJob();
            }
        }
        finally { report.FinishedAt = Now(); report.SourceAfter = await Identity(); SaveJob(); }
        return failed ? 1 : 0;
    }
}
