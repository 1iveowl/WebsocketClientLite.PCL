using System.Diagnostics;
using System.Runtime.InteropServices;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class ProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealTimeoutAndCancellationKillDescendants(bool cancel)
    {
        using var tree = new TemporaryTree();
        var marker = Path.Combine(tree.Path, "pid");
        using var cancellation = new CancellationTokenSource();
        // The descendant writes a sentinel if it survives cleanup.
        var command = new[] { "bash", "-c", "(sleep 2; touch \"$2\") & echo $! > \"$1\"; echo started; wait", "probe", marker, Path.Combine(tree.Path, "escaped") };
        if (cancel)
            cancellation.CancelAfter(500);
        var record = await Processes.Run(command, tree.Path, Path.Combine(tree.Path, "logs"), cancel ? 20 : .5, cancellation.Token);
        Assert.Equal(cancel, record.Interrupted);
        Assert.Equal(!cancel, record.TimedOut);
        Assert.Contains("started", record.Stdout);
        Assert.NotNull(record.ExitCode);
        await Task.Delay(2200);
        Assert.False(File.Exists(Path.Combine(tree.Path, "escaped")));
        var pid = int.Parse(File.ReadAllText(marker));
        Assert.False(IsRunning(pid));
    }
    [Fact]
    public async Task TimeoutAlsoKillsChildrenAfterParentExited()
    {
        using var tree = new TemporaryTree();
        var marker = Path.Combine(tree.Path, "escaped");
        var result = await Processes.Run(["bash", "-c", "(sleep 2; touch \"$1\") & exit 0", "probe", marker], tree.Path, Path.Combine(tree.Path, "logs"), .3);
        Assert.True(result.TimedOut);
        Assert.False(result.Success);
        await Task.Delay(2200);
        Assert.False(File.Exists(marker));
    }
    [Fact]
    public async Task CancellationKillsNestedSessionsBeforeParent()
    {
        using var tree = new TemporaryTree();
        using var cancellation = new CancellationTokenSource();
        var marker = Path.Combine(tree.Path, "pid");
        var command = new[] { "bash", "-c", "setsid bash -c 'echo $$ > \"$1\"; sleep 60' nested \"$1\" & wait", "probe", marker };
        var running = Processes.Run(command, tree.Path, Path.Combine(tree.Path, "logs"), 10, cancellation.Token);
        var watch = Stopwatch.StartNew();
        while (!File.Exists(marker) && watch.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(20);
        cancellation.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Interrupted);
        Assert.True(File.Exists(marker));
        Assert.False(IsRunning(int.Parse(File.ReadAllText(marker))));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapedOrphanPipeCannotHoldCleanupOpen(bool cancel)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new TemporaryTree();
        using var cancellation = new CancellationTokenSource();
        var marker = Path.Combine(tree.Path, "pid");
        // The parent exits after the child escapes its session. Tree/group kill
        // cannot discover this orphan, but its inherited output pipe stays open.
        var command = new[] { "bash", "-c", "setsid bash -c 'echo $$ > \"$1\"; exec sleep 60' orphan \"$1\" & while [ ! -s \"$1\" ]; do sleep .01; done; echo ready; exit 0", "probe", marker };
        Task<ExecutionResult>? running = null;
        try
        {
            if (cancel) cancellation.CancelAfter(500);
            running = Processes.Run(command, tree.Path, Path.Combine(tree.Path, "logs"), cancel ? 20 : .5, cancellation.Token);
            var result = await running.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(cancel, result.Interrupted);
            Assert.Equal(!cancel, result.TimedOut);
            Assert.False(result.Success);
            Assert.Contains("ready", result.Stdout);
            Assert.True(result.CleanupIncomplete);
            Assert.InRange(result.CleanupSeconds, 1.8, 4);
        }
        finally
        {
            if (File.Exists(marker))
            {
                using var child = Process.GetProcessById(int.Parse(File.ReadAllText(marker)));
                if (!child.HasExited) child.Kill(true);
            }
            if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
    static bool IsRunning(int pid)
    {
        var stat = $"/proc/{pid}/stat";
        if (!File.Exists(stat))
            return false;
        var text = File.ReadAllText(stat);
        return text[(text.LastIndexOf(')') + 2)] != 'Z';
    }
    [Fact]
    public async Task MissingExecutableHasEvidence()
    {
        using var tree = new TemporaryTree();
        var result = await Processes.Run([Path.Combine(tree.Path, "missing")], tree.Path, Path.Combine(tree.Path, "logs"), 1);
        Assert.NotNull(result.LaunchError);
        Assert.False(result.Success);
    }
    [Fact]
    public async Task SigtermCleansActualChildAndTemporarySourceTree()
    {
        using var tree = new TemporaryTree();
        var marker = Path.Combine(tree.Path, "tree");
        var dll = typeof(ProcessTests).Assembly.Location;
        using var child = Process.Start(new ProcessStartInfo("dotnet") { ArgumentList = { dll, "--cancellation-probe", marker }, RedirectStandardOutput = true, RedirectStandardError = true })!;
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(marker + ".ready") && deadline.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(20);
            Assert.True(File.Exists(marker + ".ready"), await ReadIfExited(child));
            var killed = await Processes.Output(["kill", "-TERM", child.Id.ToString()], tree.Path);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, child.ExitCode);
            Assert.False(Directory.Exists(File.ReadAllText(marker)));
            var result = Data.Read(marker + ".result");
            Assert.True(result["interrupted"]!.GetValue<bool>());
            var pid = int.Parse(File.ReadAllText(marker + ".pid"));
            Assert.False(IsRunning(pid));
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
    static async Task<string> ReadIfExited(Process process) => process.HasExited ? await process.StandardError.ReadToEndAsync() : "probe did not start";
}

// Test-only subprocess entrypoint exercises OS signal handling without adding a
// production CLI command, a helper project or a dependency on Dafny.
public static class CancellationProbe
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--cancellation-probe")
            return 2;
        using var cancellation = new CancellationTokenSource();
        using var signal = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; cancellation.Cancel(); });
        using var tree = new TemporaryTree();
        File.WriteAllText(args[1], tree.Path);
        var command = new[] { "bash", "-c", "sleep 60 & echo $! > \"$1\"; touch \"$2\"; wait", "probe", args[1] + ".pid", args[1] + ".ready" };
        var result = await Processes.Run(command, tree.Path, Path.Combine(tree.Path, "logs"), 30, cancellation.Token);
        Data.Save(args[1] + ".result", result);
        return result.Interrupted ? 0 : 1;
    }
}
