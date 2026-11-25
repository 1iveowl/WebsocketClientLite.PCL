using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    public const string CloseCallerSource = "src/main/WebsocketClientLite/Service/WebsocketSenderHandler.cs";

    // Caller-side Close defects of docs/evidence/close-caller-defects: Protocol.CloseStep stays
    // right and the shell around it in WebsocketSenderHandler is wrong. Each edit is applied
    // alone in an isolated copy and must fail Failing of the Rows rows of its named test, on the
    // named assertion text, while the other rows pass. Defects that survived every test (the
    // three lock defects and timeout-as-received) are not here: nothing can detect them yet.
    public static readonly (string Id, string Before, string After, string Class, string Test, string Assertion, int Rows, int Failing)[] CloseCallerCases =
    [
        ("send-ignored", "if (!AdvanceClose(CloseEvent.Send)) return;", "AdvanceClose(CloseEvent.Send);",
            "ControlledCloseTests", "PostWriteFailureRejectsQueuedAndLaterDataAndSuppressesClose", "The collection contained 2 items", 1, 1),
        ("send-inverted", "if (!AdvanceClose(CloseEvent.Send)) return;", "if (AdvanceClose(CloseEvent.Send)) return;",
            "ControlledCloseTests", "CancellationBetweenFragmentsAllowsOnlyClose", "Collections differ", 1, 1),
        // PeerWaitEndsByControlledTimeoutOrReadFailure also fails on this edit, but only in 28 of 30
        // runs (its AssertPending races the released disposal), so it is not the named test.
        ("release-early", "if (release) _closeReady.TrySetResult(true);", "_closeReady.TrySetResult(true);",
            "ProtocolStateTests", "CloseResponseTimeoutMakesTheTransportTerminal", "Collections differ", 1, 1),
        ("release-never", "if (release) _closeReady.TrySetResult(true);", "if (false) _closeReady.TrySetResult(true);",
            "CloseBoundaryTests", "ReadFailureReleasesReadinessBeforeObserverError", "Read failure did not release Close readiness", 1, 1),
        ("state-not-stored", "CloseState.Step(_closeState, action, out _closeState, out bool send, out bool release);",
            "CloseState.Step(_closeState, action, out CloseState discarded, out bool send, out bool release);",
            "ControlledCloseTests", "QueuedPongIsSuppressedAfterPeerClose", "The collection contained 2 items", 1, 1),
        ("read-failure-as-received", "            _sendState = TransmitState.SendFailed(_sendState);\n            AdvanceClose(CloseEvent.TimeoutOrFailure);",
            "            _sendState = TransmitState.SendFailed(_sendState);\n            AdvanceClose(CloseEvent.Received);",
            "CloseBoundaryTests", "ReadFailureReleasesReadinessBeforeObserverError", "Read failure did not release Close readiness", 1, 1),
        ("peer-close-as-failure", "lock (_stateLock) AdvanceClose(CloseEvent.Received);", "lock (_stateLock) AdvanceClose(CloseEvent.TimeoutOrFailure);",
            "ControlledCloseTests", "CancellationBetweenFragmentsAllowsOnlyClose", "Collections differ", 1, 1),
        ("peer-close-skipped", "lock (_stateLock) AdvanceClose(CloseEvent.Received);", ";",
            "ControlledCloseTests", "ParserSignalsPeerCloseOrReadFailureBeforeTimerExpiry", "Parser notification did not release Close readiness", 2, 1),
        ("read-failure-skipped", "            _sendState = TransmitState.SendFailed(_sendState);\n            AdvanceClose(CloseEvent.TimeoutOrFailure);",
            "            _sendState = TransmitState.SendFailed(_sendState);",
            "CloseBoundaryTests", "ReadFailureReleasesReadinessBeforeObserverError", "Read failure did not release Close readiness", 1, 1),
        ("timeout-skipped", "lock (_stateLock) AdvanceClose(CloseEvent.TimeoutOrFailure);\n        }\n        else", ";\n        }\n        else",
            "ProtocolStateTests", "CloseResponseTimeoutMakesTheTransportTerminal", "Collections differ", 1, 1),
        ("write-failure-not-notified", "                NotifyReadFailure();\n                throw;", "                throw;",
            "ControlledCloseTests", "PostWriteFailureRejectsQueuedAndLaterDataAndSuppressesClose", "The collection contained 4 items", 1, 1),
        ("pong-suppressed-after-own-close", "else if (op == 10 && _closeState.ReceivedOrTerminal) return;", "else if (op == 10 && _closeState.Started) return;",
            "ProtocolStateTests", "PongIsAllowedUntilPeerCloseThenSuppressed", "Collections differ", 1, 1),
        ("data-after-close-allowed", "                else if (op != 10 && _closeState.Started)\n                    throw new WebsocketClientLiteException(\"The closing handshake has started.\");\n", "",
            "ProtocolStateTests", "ReceivedCloseBlocksDataAndSendsOnlyOneResponse", "No exception was thrown", 1, 1)
    ];

    static string CloseCallerFilter(IEnumerable<(string Class, string Test)> tests) =>
        string.Join("|", tests.Distinct().Select(t => "FullyQualifiedName=WebsocketClientLiteTest." + t.Class + "." + t.Test));

    public async Task<int> CloseCallerDefects(string output, CancellationToken cancellation)
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
                    "--logger", "trx;LogFileName=results.trx", "--results-directory", Path.Combine(output, name)], Path.Combine(output, name), 300, cancellation);
            report["baseline"] = Node(await Run("baseline", CloseCallerFilter(CloseCallerCases.Select(c => (c.Class, c.Test)))));
            Require(Passed(report["baseline"]), "Close caller baseline failed");
            var file = Path.Combine(copy.Path, CloseCallerSource);
            var original = Text(file);
            foreach (var c in CloseCallerCases)
            {
                var index = original.IndexOf(c.Before, StringComparison.Ordinal);
                Require(index >= 0 && original.IndexOf(c.Before, index + 1, StringComparison.Ordinal) < 0, "Ambiguous/missing close caller edit: " + c.Id);
                File.WriteAllText(file, original[..index] + c.After + original[(index + c.Before.Length)..]);
                var run = await Run(c.Id, CloseCallerFilter([(c.Class, c.Test)]));
                var experiment = new JsonObject { ["id"] = c.Id, ["status"] = "failed", ["test"] = c.Class + "." + c.Test, ["assertion"] = c.Assertion,
                    ["source_hash"] = Hash(System.Text.Encoding.UTF8.GetBytes(original)), ["mutant_hash"] = Hash(File.ReadAllBytes(file)),
                    ["before"] = c.Before, ["after"] = c.After, ["run"] = Node(run) };
                report["experiments"]!.AsArray().Add(experiment);
                File.WriteAllText(file, original);
                experiment["host_runtime"] = ValidateCloseCallerResult(Path.Combine(output, c.Id), Node(run), c);
                experiment["status"] = "passed";
            }
            report["status"] = "passed";
            ValidateCloseCallerReport(output, report, (await Identity()).SourceHashes[CloseCallerSource]!);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { report["status"] = "failed"; report["error"] = e.Message; }
        finally { report["finished_at"] = Now(); report["source_after"] = Node(await Identity()); Save(Path.Combine(output, "results.json"), report); }
        return S(report, "status") == "passed" ? 0 : 1;
    }

    public static void ValidateCloseCallerReport(string directory, JsonNode report, string sourceHash)
    {
        Require(S(report, "status") == "passed" && S(report, "library_target") == PrimaryTfm && Passed(report["baseline"]), "Incomplete close caller baseline/report");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        ValidateAssertionExecution(Path.Combine(directory, "baseline"), 0);
        var baseline = XDocument.Load(Path.Combine(directory, "baseline/results.trx")).Descendants(ns + "UnitTestResult").ToArray();
        var tests = CloseCallerCases.Select(c => (c.Class, c.Test, c.Rows)).Distinct().ToArray();
        Require(baseline.Length == tests.Sum(t => t.Rows) + 1 && baseline.All(r => (string?)r.Attribute("outcome") == "Passed"), "Incomplete close caller baseline cases");
        foreach (var (cls, test, rows) in tests)
            Require(baseline.Count(r => ((string?)r.Attribute("testName"))?.Contains(cls + "." + test, StringComparison.Ordinal) == true) == rows, "Missing named close caller baseline case");
        var baselineRuntime = baseline.Single(r => ((string?)r.Attribute("testName"))?.EndsWith("RecordHostRuntime", StringComparison.Ordinal) == true).Value;
        var entries = report["experiments"]!.AsArray();
        Require(entries.Count == CloseCallerCases.Length, "Missing close caller experiments");
        foreach (var c in CloseCallerCases)
        {
            var e = entries.Single(e => S(e, "id") == c.Id)!;
            Require(S(e, "status") == "passed" && S(e, "source_hash") == sourceHash && S(e, "mutant_hash").Length == 64 && S(e, "mutant_hash") != sourceHash, "Close caller mutation identity mismatch");
            Require(S(e, "before") == c.Before && S(e, "after") == c.After && S(e, "test") == c.Class + "." + c.Test && S(e, "assertion") == c.Assertion, "Close caller experiment contract mismatch");
            var runtime = ValidateCloseCallerResult(Path.Combine(directory, c.Id), e["run"]!, c);
            Require(S(e, "host_runtime") == runtime && baselineRuntime.Contains("PDD_HOST_RUNTIME=" + runtime, StringComparison.Ordinal), "Close caller runtime mismatch");
        }
    }

    // A detection is the named test failing on the named assertion in exactly Failing of its Rows
    // rows, with every other row passing; a timeout, a build failure or another message is not.
    static string ValidateCloseCallerResult(string directory, JsonNode run,
        (string Id, string Before, string After, string Class, string Test, string Assertion, int Rows, int Failing) c)
    {
        Require(S(run, "status") == "failed" && run["exit_code"]?.ToString() == "1", "Close caller mutant must fail assertions, not timeout or compilation");
        ValidateAssertionExecution(directory, 1);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var results = XDocument.Load(Path.Combine(directory, "results.trx")).Descendants(ns + "UnitTestResult").ToArray();
        Require(results.Length == c.Rows + 1, "Missing close caller result rows");
        var named = results.Where(r => ((string?)r.Attribute("testName"))?.Contains(c.Class + "." + c.Test, StringComparison.Ordinal) == true).ToArray();
        var failed = named.Where(r => (string?)r.Attribute("outcome") == "Failed").ToArray();
        Require(named.Length == c.Rows && failed.Length == c.Failing && failed.All(r => r.Value.Contains(c.Assertion, StringComparison.Ordinal)
            && !r.Value.Contains("TimeoutException", StringComparison.Ordinal)), "Missing intended close caller assertion");
        Require(named.Except(failed).All(r => (string?)r.Attribute("outcome") == "Passed"), "Incomplete close caller cases");
        var runtime = results.Single(r => ((string?)r.Attribute("testName"))?.EndsWith("RecordHostRuntime", StringComparison.Ordinal) == true);
        var match = System.Text.RegularExpressions.Regex.Match(runtime.Value, @"PDD_HOST_RUNTIME=(.+)");
        Require((string?)runtime.Attribute("outcome") == "Passed" && match.Success, "Missing close caller host runtime");
        return match.Groups[1].Value.Trim();
    }
}
