using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pdd;

public static class Processes
{
    // Linux sessions preserve cleanup even when a shell exits before its children.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    static extern int KillProcessGroup(int pid, int signal);
    // All commands use argv, bounded execution, streamed logs and tree cleanup.
    public static async Task<ExecutionResult> Run(string[] command, string cwd, string directory, double budget, CancellationToken cancellation = default, IDictionary<string, string>? environment = null, string? combinedLog = null)
    {
        Data.Require(double.IsFinite(budget) && budget > 0, "budgets must be positive and finite");
        Directory.CreateDirectory(directory);
        var record = new ExecutionResult { Command = command, BudgetSeconds = budget };
        var watch = Stopwatch.StartNew();
        using var stdout = new StreamWriter(Path.Combine(directory, "stdout.log"));
        using var stderr = new StreamWriter(Path.Combine(directory, "stderr.log"));
        using var combined = combinedLog is null ? null : new StreamWriter(combinedLog);
        var gate = new SemaphoreSlim(1);
        async Task Pump(StreamReader input, StreamWriter output)
        {
            var buffer = new char[4096];
            int count;
            while ((count = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count));
                await output.FlushAsync();
                if (combined is not null)
                {
                    await gate.WaitAsync();
                    try
                    {
                        await combined.WriteAsync(buffer.AsMemory(0, count));
                        await combined.FlushAsync();
                    }
                    finally { gate.Release(); }
                }
            }
        }
        var grouped = OperatingSystem.IsLinux();
        using var process = new Process { StartInfo = new ProcessStartInfo(grouped ? "setsid" : command[0]) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (var arg in grouped ? command : command.Skip(1))
            process.StartInfo.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (key, value) in environment)
                process.StartInfo.Environment[key] = value;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            process.Start();
            var output = Pump(process.StandardOutput, stdout);
            var error = Pump(process.StandardError, stderr);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(budget));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token);
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                record.Interrupted = cancellation.IsCancellationRequested;
                record.TimedOut = !record.Interrupted;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                if (grouped)
                    KillProcessGroup(-process.Id, 9);
                await process.WaitForExitAsync();
            }
            record.ExitCode = process.ExitCode;
            await Task.WhenAll(output, error);
        }
        catch (OperationCanceledException) { record.Interrupted = true; }
        catch (System.ComponentModel.Win32Exception error) { record.LaunchError = error.Message; await stderr.WriteAsync(error.Message); if (combined is not null) await combined.WriteAsync(error.Message); }
        await stdout.FlushAsync();
        await stderr.FlushAsync();
        if (combined is not null)
            await combined.FlushAsync();
        record.ElapsedSeconds = Math.Round(watch.Elapsed.TotalSeconds, 3);
        record.Stdout = Data.Text(Path.Combine(directory, "stdout.log"));
        record.Stderr = Data.Text(Path.Combine(directory, "stderr.log"));
        if (grouped && record.ExitCode is 126 or 127 && record.Stderr.StartsWith("setsid: failed to execute ", StringComparison.Ordinal))
        {
            record.LaunchError = record.Stderr.Trim();
            record.ExitCode = null;
        }
        return record;
    }
    public static async Task<string> Output(string[] command, string cwd, CancellationToken cancellation = default)
    {
        using var temporary = new TemporaryTree();
        var result = await Run(command, cwd, temporary.Path, 60, cancellation);
        Data.Require(result.Success, result.Stderr.Trim() is { Length: > 0 } message ? message : $"{command[0]} failed or interrupted");
        return result.Stdout;
    }
}
public sealed class TemporaryTree : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pdd-" + Guid.NewGuid().ToString("N"));
    public TemporaryTree() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}
