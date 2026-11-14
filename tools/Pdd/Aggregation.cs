using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Pdd.Data;

namespace Pdd;

public sealed partial class Evidence
{
    static string S(JsonNode? node, string key, string fallback = "") => node is JsonObject obj ? obj[key]?.ToString() ?? fallback : fallback;
    static bool Passed(JsonNode? run) => S(run, "status") == "passed" && run?["exit_code"]?.ToString() == "0" && S(run, "started_at").Length > 0 && S(run, "finished_at").Length > 0;
    static bool Equal(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
    static string LocalFile(string root, string name)
    {
        var path = Path.GetFullPath(Path.Combine(root, name));
        Require(!Path.IsPathRooted(name) && path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "evidence path outside directory");
        // Also reject symlink escapes as for source references.
        Requirements.Source(root, name);
        return path;
    }
    public static JsonObject TestResults(string directory)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var matrix = new JsonObject();
        foreach (var tfm in Tfms)
        {
            var path = Path.Combine(directory, "tests", tfm);
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.trx") : [];
            if (files.Length != 1)
            {
                matrix[tfm] = new JsonObject { ["status"] = "not-run", ["tests"] = new JsonObject(), ["host_runtime"] = null };
                continue;
            }
            var tree = XDocument.Load(files[0]);
            var definitions = tree.Descendants(ns + "UnitTest").ToDictionary(e => (string)e.Attribute("id")!, e => e.Element(ns + "TestMethod"));
            var tests = new Dictionary<string, string>();
            string? runtime = null;
            JsonNode? host = null;
            var caseCounts = new Dictionary<string, int>();
            Dictionary<string, int> severity = new()
            {
                ["passed"] = 0,
                ["skipped"] = 1,
                ["not-run"] = 2,
                ["failed"] = 3
            };
            foreach (var item in tree.Descendants(ns + "UnitTestResult"))
            {
                Require(definitions.TryGetValue((string)item.Attribute("testId")!, out var method) && method is not null, "TRX result has no test definition");
                var name = ((string)method!.Attribute("className")!).Split(',')[0] + "." + (string)method.Attribute("name")!;
                caseCounts[name] = caseCounts.GetValueOrDefault(name) + 1;
                var status = (string?)item.Attribute("outcome") switch
                {
                    "Passed" => "passed",
                    "NotExecuted" => "skipped",
                    _ => "failed"
                };
                if (!tests.TryGetValue(name, out var prior) || severity[status] > severity[prior])
                    tests[name] = status;
                if (name == "WebsocketClientLiteTest.RuntimeEvidenceTests.RecordHostRuntime" && status == "passed")
                {
                    var match = Regex.Match(item.Value, @"PDD_HOST_RUNTIME=(.+)");
                    runtime = match.Success ? match.Groups[1].Value.Trim() : null;
                    var identity = Regex.Match(item.Value, @"PDD_HOST_IDENTITY=(.+)");
                    if (identity.Success) host = Parse(identity.Groups[1].Value.Trim());
                }
            }
            foreach (var (name, count) in caseCounts.Where(p => p.Key.StartsWith("WebsocketClientLiteTest.ControlledCloseTests.", StringComparison.Ordinal) || p.Key.StartsWith("WebsocketClientLiteTest.CloseBoundaryTests.", StringComparison.Ordinal)))
            {
                var expectedCases = name.EndsWith("PausedCloseWriteBlocksDisposalAndPeerRaceSendsOnce", StringComparison.Ordinal) || name.EndsWith("PeerWaitEndsByControlledTimeoutOrReadFailure", StringComparison.Ordinal) || name.EndsWith("ParserSignalsPeerCloseOrReadFailureBeforeTimerExpiry", StringComparison.Ordinal) || name.EndsWith("DisposalStopsActivePeriodicPingsBeforeClose", StringComparison.Ordinal) ? 2 : 1;
                if (count != expectedCases) tests[name] = "failed";
            }
            foreach (var (name, count) in QueuedCaseCounts)
            {
                var fullName = "WebsocketClientLiteTest.QueuedWriteTests." + name;
                if (caseCounts.TryGetValue(fullName, out var actualCount) && actualCount != count) tests[fullName] = "failed";
            }
            foreach (var (name, count) in LeaseCaseCounts)
            {
                var fullName = "WebsocketClientLiteTest.PayloadLeaseTests." + name;
                if (caseCounts.TryGetValue(fullName, out var actualCount) && actualCount != count) tests[fullName] = "failed";
            }
            foreach (var (name, count) in RetentionCaseCounts)
            {
                var fullName = "WebsocketClientLiteTest.ParserRetentionTests." + name;
                if (caseCounts.TryGetValue(fullName, out var actualCount) && actualCount != count) tests[fullName] = "failed";
            }
            var run = Read(Path.Combine(path, "run.json"));
            var valid = Passed(run) && (run["command"]?.AsArray() ?? []).Any(c => c?.ToString() == "-p:LibTfm=" + tfm) && File.Exists(Path.Combine(path, "output.log"));
            matrix[tfm] = new JsonObject { ["status"] = valid && tests.Count > 0 && tests.Values.All(x => x == "passed") && !string.IsNullOrEmpty(runtime) ? "passed" : "failed", ["execution"] = run, ["tests"] = Node(tests), ["host_runtime"] = runtime, ["host"] = host, ["case_counts"] = Node(caseCounts), ["trx"] = Path.GetRelativePath(directory, files[0]) };
        }
        return matrix;
    }
    public JsonObject Aggregate(string directory, SourceIdentity expected)
    {
        var errors = new JsonArray();
        var jobs = new JsonObject();
        var rejected = new JsonObject();
        var requirements = new JsonArray();
        var reviews = new JsonArray();
        var ci = Node(CiIdentity());
        var receipt = new JsonObject { ["schema_version"] = 1, ["created_at"] = Now(), ["source"] = Node(expected), ["ci"] = ci, ["jobs"] = jobs, ["rejected_jobs"] = rejected, ["errors"] = errors, ["requirements"] = requirements, ["review_evidence"] = reviews, ["stryker"] = new JsonObject { ["status"] = "not-run", ["reason"] = "Missing separate Stryker job evidence" } };
        foreach (var job in new[] { "protocol", "tests", "controlled", "stryker" })
        {
            var path = Path.Combine(directory, job, "job.json");
            if (!File.Exists(path))
            {
                errors.Add($"Missing {job} job evidence");
                continue;
            }
            JsonNode? data = null;
            try
            {
                data = Read(path);
                Require(data["schema_version"]?.ToString() == "1" && S(data, "job") == job && Equal(data["source"], Node(expected)) && Equal(data["source_after"], Node(expected)), "source identity mismatch or incomplete run");
                foreach (var key in new[] { "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_REPOSITORY", "GITHUB_EVENT_NAME", "GITHUB_SHA" })
                    Require(Equal(data["ci"]![key], ci[key]), "CI identity mismatch");
                jobs[job] = data;
                foreach (var (name, _) in Commands(job, Path.Combine(directory, job)))
                {
                    var run = data["runs"]?[name];
                    var valid = Passed(run);
                    try
                    {
                        LocalFile(Path.Combine(directory, job), S(run, "log", "missing"));
                    }
                    catch { valid = false; }
                    if (!valid)
                    {
                        errors.Add($"{job}/{name}: missing or unsuccessful command evidence");
                        // A claimed pass with incomplete execution evidence is not a proof pass.
                        if (run is JsonObject && S(run, "status") == "passed")
                            run["status"] = "failed";
                    }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                jobs.Remove(job);
                rejected[job] = new JsonObject { ["reason"] = error.Message, ["partial_evidence"] = data?.DeepClone() };
                errors.Add($"{job}: {error.Message}");
            }
        }
        var controlled = new JsonObject { ["status"] = "not-run" };
        receipt["controlled_experiment"] = controlled;
        if (jobs.ContainsKey("controlled"))
        {
            try
            {
                Require(Passed(jobs["controlled"]?["runs"]?["defect"]), "unsuccessful defect command");
                var defect = Read(Path.Combine(directory, "controlled/defect/results.json"));
                Require(Equal(defect["source"], Node(expected)) && Equal(defect["source_after"], Node(expected)), "defect source mismatch");
                Require(S(defect, "status") == "passed" && S(defect, "library_target") == PrimaryTfm, "incomplete defect experiment");
                var runtime = ValidateDefect(Path.Combine(directory, "controlled/defect"), defect["baseline"]!, defect["mutant"]!);
                controlled["status"] = "passed"; controlled["host_runtime"] = runtime; controlled["evidence"] = defect;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { controlled["status"] = "failed"; errors.Add("Controlled experiment: " + e.Message); }
        }
        var boundaries = new JsonObject { ["status"] = "not-run" };
        receipt["boundary_experiments"] = boundaries;
        if (jobs.ContainsKey("controlled"))
        {
            try
            {
                Require(Passed(jobs["controlled"]?["runs"]?["boundaries"]), "unsuccessful boundary command");
                var directoryPath = Path.Combine(directory, "controlled/boundaries");
                var report = Read(Path.Combine(directoryPath, "results.json"));
                Require(Equal(report["source"], Node(expected)) && Equal(report["source_after"], Node(expected)), "boundary source mismatch");
                ValidateBoundaryReport(directoryPath, report, expected.SourceHashes[CloseSource]!);
                boundaries["status"] = "passed"; boundaries["evidence"] = report;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { boundaries["status"] = "failed"; errors.Add("Boundary experiments: " + e.Message); }
        }
        var queued = new JsonObject { ["status"] = "not-run" };
        receipt["queued_experiment"] = queued;
        if (jobs.ContainsKey("controlled"))
        {
            try
            {
                Require(Passed(jobs["controlled"]?["runs"]?["queued"]), "unsuccessful queued command");
                var directoryPath = Path.Combine(directory, "controlled/queued");
                var report = Read(Path.Combine(directoryPath, "results.json"));
                Require(Equal(report["source"], Node(expected)) && Equal(report["source_after"], Node(expected)) && Equal(report["ci"], jobs["controlled"]?["ci"]), "queued source/CI mismatch");
                ValidateQueuedReport(directoryPath, report, expected.SourceHashes[QueuedSource]!);
                queued["status"] = "passed"; queued["evidence"] = report;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { queued["status"] = "failed"; errors.Add("Queued experiment: " + e.Message); }
        }
        var leases = new JsonObject { ["status"] = "not-run" };
        receipt["leases_experiment"] = leases;
        if (jobs.ContainsKey("controlled"))
        {
            try
            {
                Require(Passed(jobs["controlled"]?["runs"]?["leases"]), "unsuccessful leases command");
                var directoryPath = Path.Combine(directory, "controlled/leases");
                var report = Read(Path.Combine(directoryPath, "results.json"));
                Require(Equal(report["source"], Node(expected)) && Equal(report["source_after"], Node(expected)) && Equal(report["ci"], jobs["controlled"]?["ci"]), "leases source/CI mismatch");
                Require(S(report, "status") == "passed", "Incomplete lease report");
                ValidateLeaseReport(directoryPath, report, expected.SourceHashes[LeaseSource]!);
                leases["status"] = "passed"; leases["evidence"] = report;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { leases["status"] = "failed"; errors.Add("Lease experiment: " + e.Message); }
        }
        var retention = new JsonObject { ["status"] = "not-run" };
        receipt["retention_experiment"] = retention;
        if (jobs.ContainsKey("controlled"))
        {
            try
            {
                Require(Passed(jobs["controlled"]?["runs"]?["retention"]), "unsuccessful retention command");
                var directoryPath = Path.Combine(directory, "controlled/retention");
                var report = Read(Path.Combine(directoryPath, "results.json"));
                Require(Equal(report["source"], Node(expected)) && Equal(report["source_after"], Node(expected)) && Equal(report["ci"], jobs["controlled"]?["ci"]), "retention source/CI mismatch");
                Require(S(report, "status") == "passed", "Incomplete lease report");
                ValidateRetentionReport(directoryPath, report, expected.SourceHashes);
                retention["status"] = "passed"; retention["evidence"] = report;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { retention["status"] = "failed"; errors.Add("Retention experiment: " + e.Message); }
        }
        var reconnect = new JsonObject { ["status"] = "not-run" };
        receipt["reconnect_experiment"] = reconnect;
        if (jobs.ContainsKey("controlled"))
        {
            try
            {
                Require(Passed(jobs["controlled"]?["runs"]?["reconnect"]), "unsuccessful reconnect command");
                var directoryPath = Path.Combine(directory, "controlled/reconnect");
                var report = Read(Path.Combine(directoryPath, "results.json"));
                Require(Equal(report["source"], Node(expected)) && Equal(report["source_after"], Node(expected)) && Equal(report["ci"], jobs["controlled"]?["ci"]), "reconnect source/CI mismatch");
                Require(S(report, "status") == "passed", "Incomplete reconnect report");
                ValidateReconnectReport(directoryPath, report, expected.SourceHashes);
                reconnect["status"] = "passed"; reconnect["evidence"] = report;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { reconnect["status"] = "failed"; errors.Add("Reconnect experiment: " + e.Message); }
        }
        if (jobs.ContainsKey("stryker"))
        {
            try
            {
                Require(Passed(jobs["stryker"]?["runs"]?["stryker"]) && Passed(jobs["stryker"]?["runs"]?["runtime"]), "unsuccessful Stryker command/runtime probe");
                receipt["stryker"] = StrykerEvidence(Path.Combine(directory, "stryker"), expected);
            }
            catch (Exception e) when (e is not OutOfMemoryException) { receipt["stryker"] = new JsonObject { ["status"] = "failed", ["reason"] = e.Message }; errors.Add("Stryker: " + e.Message); }
        }
        var proof = jobs["protocol"]?["runs"]?["proof"];
        if (S(proof, "status") == "passed")
        {
            var log = Path.Combine(directory, "protocol/proof.log");
            var text = File.Exists(log) ? Text(log) : "";
            var summaries = Regex.Matches(text, @"Dafny program verifier finished with (\d+) verified, (\d+) errors?");
            if (summaries.Count == 0 || summaries.Any(m => int.Parse(m.Groups[1].Value) == 0 || int.Parse(m.Groups[2].Value) != 0) || Regex.IsMatch(text, @"Error:|timed out|time out|timeout|resource limit|inconclusive", RegexOptions.IgnoreCase))
            {
                proof!["status"] = "failed";
                errors.Add("Missing or failed verifier summary");
            }
        }
        var matrix = new JsonObject();
        JsonObject mutations = new();
        try
        {
            if (jobs.ContainsKey("tests"))
            {
                matrix = TestResults(Path.Combine(directory, "tests"));
                foreach (var cell in HostCells.Where(c => c.Placement == "mandatory"))
                {
                    try { ValidateHostIdentity(cell, matrix[cell.LibraryTfm]?["host"], S(matrix[cell.LibraryTfm], "host_runtime")); }
                    catch (Exception e) when (e is not OutOfMemoryException) { errors.Add(cell.Id + ": " + e.Message); }
                }
                if (matrix.Any(p => S(p.Value, "status") != "passed"))
                    errors.Add("Incomplete or unsuccessful framework matrix/runtime evidence");
            }
            if (jobs.ContainsKey("protocol"))
            {
                mutations = Read(Path.Combine(directory, "protocol/mutations/results.json")).AsObject();
                var versions = Read(Path.Combine(directory, "protocol/mutations/tool-versions.json"));
                foreach (var (name, version) in new[] { ("dafny", "4.11.0"), ("z3", "4.12.1") })
                {
                    var probe = versions[name];
                    Require(probe is JsonObject && S(probe, "stdout").Contains(version), "missing or mismatched verifier version evidence");
                    if (probe!["exit_code"] is { } exit)
                        Require(exit.ToString() == "0", "failed verifier version probe");
                    Require(probe["timed_out"]?.GetValue<bool>() != true && probe["interrupted"]?.GetValue<bool>() != true && probe["launch_error"] is null, "incomplete verifier version probe");
                }
                jobs["protocol"]!["tools"]!["verifier"] = versions;
                var hashes = mutations["source_hashes"]!.AsObject();
                Require(hashes.Select(p => p.Key).ToHashSet().SetEquals(["verification/Masking.dfy", "verification/Protocol.dfy"]), "missing mutation source hashes");
                Require(hashes.All(p => expected.SourceHashes.TryGetValue(p.Key, out var hash) && p.Value?.ToString() == hash), "mutation source hashes mismatch");
                Require(mutations["experiments"] is JsonArray, "missing experiments");
                foreach (var experiment in mutations["experiments"]!.AsArray())
                {
                    Require(experiment is JsonObject, "invalid mutation experiment");
                    Require(experiment!["timed_out"]?.GetValue<bool>() != true && experiment["interrupted"]?.GetValue<bool>() != true && experiment["launch_error"] is null, "incomplete mutation execution");
                    if (experiment!["matched_clauses"] is { } clauses)
                        Require(clauses is JsonArray list && list.All(c => c is JsonValue value && value.TryGetValue<string>(out _)), "invalid matched clauses");
                }
                var ids = mutations["experiments"]!.AsArray().Select(e => e!["id"]!.ToString()).ToArray();
                Require(ids.Distinct().Count() == ids.Length, "duplicate mutation experiment");
                if (S(mutations, "status") != "passed")
                    errors.Add("Mutation suite failed");
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { errors.Add($"Invalid raw evidence: {error.Message}"); mutations = new(); }
        var coverage = new JsonObject { ["status"] = "not-run", ["reports"] = new JsonArray() };
        receipt["coverage"] = coverage;
        if (jobs.ContainsKey("tests"))
        {
            try
            {
                var path = Path.Combine(directory, "tests/coverage");
                var reports = Directory.Exists(path) ? Directory.GetFiles(path, "coverage.cobertura.xml", SearchOption.AllDirectories) : [];
                Require(reports.Length > 0, "missing coverage report");
                foreach (var file in reports)
                {
                    var rate = double.Parse((string)XDocument.Load(file).Root!.Attribute("line-rate")!, System.Globalization.CultureInfo.InvariantCulture);
                    Require(rate >= .70 && rate <= 1, "coverage below 70% or invalid rate");
                    coverage["reports"]!.AsArray().Add(new JsonObject { ["path"] = Path.GetRelativePath(directory, file), ["line_rate"] = rate });
                }
                coverage["status"] = "passed";
            }
            catch (Exception error) when (error is not OutOfMemoryException) { coverage["status"] = "failed"; errors.Add($"Coverage: {error.Message}"); }
        }
        receipt["framework_matrix"] = matrix;
        receipt["dafny_mutations"] = mutations;
        var manifest = Model<RequirementManifest>(Read(Path.Combine(root, "verification/requirements.json")));
        var catalog = new Mutations(root).LoadCatalog().Mutants;
        foreach (var requirement in manifest.Requirements)
        {
            var obligations = new List<JsonObject>();
            foreach (var o in requirement.Obligations)
            {
                var item = Node(o).AsObject();
                item["status"] = "not-run";
                item["evidence"] = new JsonArray();
                if (o.Kind == "review")
                {
                    item["reason"] = "Acceptance must be recorded against the revision in PR review; textual reports are not approval.";
                    reviews.Add(item.DeepClone());
                }
                else if (o.Kind == "proof")
                {
                    item["status"] = S(proof, "status", "not-run");
                    item["evidence"] = Node(new[] { "protocol/proof.log" });
                }
                else if (o.Kind == "mutation")
                {
                    var required = catalog.Where(m => m.Obligation == o.Id).ToArray();
                    var experiments = (mutations["experiments"] as JsonArray ?? []).ToDictionary(e => e!["id"]!.ToString(), e => e!);
                    experiments.TryGetValue("baseline", out var baseline);
                    var ok = S(baseline, "status") == "passed" && baseline?["exit_code"]?.ToString() == "0";
                    ok &= required.All(m => experiments.TryGetValue(m.Id, out var e) && S(e, "status") == "detected" && e["exit_code"]?.ToString() == "4" && (e["matched_clauses"]?.AsArray() ?? []).Any(c => c?.ToString() == m.TargetClause));
                    item["status"] = required.Length > 0 && ok ? "passed" : mutations.Count > 0 ? "failed" : "not-run";
                    item["evidence"] = Node(new[] { "protocol/mutations/results.json" });
                }
                else if (o.Kind == "test")
                {
                    var statuses = Tfms.SelectMany(t => o.References.Select(r => S(matrix[t]?["tests"], r.Symbol, "not-run"))).ToArray();
                    item["status"] = statuses.Contains("failed") ? "failed" : statuses.Length == 0 || statuses.Contains("not-run") ? "not-run" : statuses.Contains("skipped") ? "skipped" : "passed";
                    item["evidence"] = Node(Tfms.Select(t => $"tests/tests/{t}/results.trx").ToArray());
                }
                if (o.Id == "WRITE-001-TEST")
                {
                    item["evidence"]!.AsArray().Add("controlled/queued/results.json");
                    if (S(queued, "status") != "passed") item["status"] = S(queued, "status");
                }
                if (o.Id == "LEASE-001-TEST")
                {
                    item["evidence"]!.AsArray().Add("controlled/leases/results.json");
                    if (S(leases, "status") != "passed") item["status"] = S(leases, "status");
                }
                if (o.Id == "REASSEMBLY-001-TEST")
                {
                    item["evidence"]!.AsArray().Add("controlled/retention/results.json");
                    if (S(retention, "status") != "passed") item["status"] = S(retention, "status");
                }
                if (o.Id == "EPOCH-001-TEST")
                {
                    item["evidence"]!.AsArray().Add("controlled/reconnect/results.json");
                    if (S(reconnect, "status") != "passed") item["status"] = S(reconnect, "status");
                }
                if (o.Id == "CLOSE-004-CONTROLLED")
                {
                    item["evidence"]!.AsArray().Add("controlled/defect/results.json");
                    if (S(controlled, "status") != "passed") item["status"] = S(controlled, "status");
                }
                if (o.Id is "CLOSE-004-CONTROLLED" or "CLOSE-005-CONTROLLED")
                {
                    item["evidence"]!.AsArray().Add("controlled/boundaries/results.json");
                    if (S(boundaries, "status") != "passed") item["status"] = S(boundaries, "status");
                }
                if (o.Kind != "review" && S(item, "status") != "passed")
                    errors.Add($"{o.Id}: {item["status"]}");
                obligations.Add(item);
            }
            requirements.Add(new JsonObject { ["id"] = requirement.Id, ["satisfied"] = obligations.All(o => S(o, "status") == "passed"), ["machine_evidence"] = new JsonArray(obligations.Where(o => S(o, "kind") != "review").Select(o => (JsonNode)o).ToArray()) });
        }
        receipt["gate"] = new JsonObject { ["status"] = errors.Count > 0 ? "failed" : "passed", ["scope"] = "Machine obligations, controlled ordering and boundary experiments, recorded Stryker dispositions and existing CI gates; PR review remains separate and unsatisfied." };
        return receipt;
    }
    public static string Summary(JsonObject receipt)
    {
        var lines = new List<string> { "# PDD verification receipt", "", $"Machine evidence gate: **{receipt["gate"]!["status"]}**", "", receipt["gate"]!["scope"]!.ToString(), "", $"Tested commit: `{receipt["source"]!["tested_commit"]}`", $"PR source head: `{receipt["source"]!["pr_source_head"]}`", "", $"{Tfms.Length} library builds use the actual hosts recorded below; they are not {Tfms.Length} independently tested runtimes.", "" };
        foreach (var (tfm, result) in receipt["framework_matrix"]!.AsObject())
            lines.Add($"- {tfm}: {result!["status"]}; host: {result["host_runtime"]}");
        lines.AddRange(["", "| Obligation | Status |", "| --- | --- |"]);
        foreach (var req in receipt["requirements"]!.AsArray())
            foreach (var item in req!["machine_evidence"]!.AsArray())
                lines.Add($"| {item!["id"]} | {item["status"]} {string.Join(", ", item["evidence"]!.AsArray().Select(p => $"[{p}]({p})"))} |");
        lines.AddRange(["", "Review evidence is separate and not-run; no requirement is claimed satisfied without it.", $"Stryker: {receipt["stryker"]!["status"]}; controlled ordering experiment: {receipt["controlled_experiment"]!["status"]}; boundary experiments: {receipt["boundary_experiments"]!["status"]}.", "", "Receipts are traceability records, not signed attestations.", ""]);
        lines.AddRange(receipt["errors"]!.AsArray().Select(e => $"- {e}"));
        return string.Join('\n', lines) + "\n";
    }
}
