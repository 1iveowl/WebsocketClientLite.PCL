using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Pdd.Data;

namespace Pdd;

public sealed class Mutations(string root)
{
    public MutationCatalog LoadCatalog(JsonNode? data = null)
    {
        data ??= Read(Path.Combine(root, "verification/mutations/close.json"));
        Require(data is JsonObject && data.AsObject().Select(p => p.Key).ToHashSet().SetEquals(["schema_version", "mutants"]) && data["schema_version"]?.GetValue<int>() == 1, "unsupported catalog shape/version");
        var manifest = new Requirements(root).Validate();
        var ids = new HashSet<string>();
        Require(data["mutants"] is JsonArray { Count: > 0 }, "empty catalog");
        string[] fields = ["id", "requirement", "obligation", "file", "symbol", "before", "after", "target_clause", "allowed_clauses", "classification"];
        foreach (var item in data["mutants"]!.AsArray())
        {
            Require(item is JsonObject && item.AsObject().Select(p => p.Key).ToHashSet().SetEquals(fields), "invalid mutant fields");
            Require(fields.Where(k => k != "allowed_clauses").All(k => item![k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0), "empty mutant field");
            Require(item!["allowed_clauses"] is JsonArray a && a.All(c => c is JsonValue v && v.TryGetValue<string>(out _)), "missing target clause");
        }
        var catalog = Model<MutationCatalog>(data);
        foreach (var m in catalog.Mutants)
        {
            Require(Regex.IsMatch(m.Id, @"^CLOSE-[0-9]{3}-[a-z-]+$") && ids.Add(m.Id), "invalid/duplicate mutant ID");
            Require(m.File == "verification/Protocol.dfy" && m.Symbol == "ProtocolCore.Protocol.CloseStep", "unsupported mutation scope");
            var req = manifest.Requirements.SingleOrDefault(r => r.Id == m.Requirement);
            Require(req is not null && req.Obligations.Any(o => o.Id == m.Obligation && o.Kind == "mutation"), "dangling mutation obligation");
            Require(req!.Contracts.Any(c => c.Clauses.Contains(m.TargetClause)), "target not mapped to requirement");
            var header = Requirements.Resolve(root, m).Split('\n').Select(l => l.Trim()).ToArray();
            Require(m.AllowedClauses.Contains(m.TargetClause), "missing target clause");
            Require(m.AllowedClauses.All(c => c.StartsWith("ensures ") && header.Contains(c)), "unresolved allowed clause");
            Require(m.Classification == "postcondition", "unsupported classification");
            Apply(Requirements.Source(root, m.File), m);
        }
        foreach (var o in manifest.Requirements.SelectMany(r => r.Obligations).Where(o => o.Kind == "mutation" && o.Delivery is "B" or "existing"))
            Require(catalog.Mutants.Any(m => m.Obligation == o.Id), "uncovered mutation obligation");
        return catalog;
    }
    public static string Apply(string source, Mutant m)
    {
        var method = source.IndexOf("static method CloseStep(", StringComparison.Ordinal);
        Require(method >= 0, "missing CloseStep");
        var start = source.IndexOf('{', method) + 1;
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Require(start > 0 && end > start, "missing CloseStep body");
        Require(!(m.Before + m.After).Contains('\n') && m.Before != m.After && source.Split(m.Before).Length == 2, "mutation must be a unique, changed single line");
        Require(source[start..end].Contains(m.Before) && !m.After.Any(c => c is '{' or '}'), "mutation outside CloseStep body");
        return source.Replace(m.Before, m.After);
    }
    public static MutationOutcome Classify(ExecutionResult record, string source, Mutant? mutant = null)
    {
        MutationOutcome Inconclusive() => new("inconclusive", []);
        if (record.TimedOut || record.Interrupted || record.LaunchError is not null)
            return Inconclusive();
        var output = record.Stdout + "\n" + record.Stderr;
        var summaries = Regex.Matches(output, @"Dafny program verifier finished with (\d+) verified, (\d+) errors?");
        if (summaries.Count != 1 || Regex.IsMatch(output, @"timed out|timeout|resource limit|inconclusive", RegexOptions.IgnoreCase))
            return Inconclusive();
        var verified = int.Parse(summaries[0].Groups[1].Value);
        var errors = int.Parse(summaries[0].Groups[2].Value);
        if (record.ExitCode == 0 && errors == 0 && verified > 0 && !output.Contains("Error:"))
            return new(mutant is null ? "passed" : "survived", []);
        if (mutant is null || record.ExitCode != 4 || errors == 0)
            return Inconclusive();
        var primary = Regex.Matches(output, @"^(.+?)\((\d+),(\d+)\): Error: (.+)$", RegexOptions.Multiline);
        var related = Regex.Matches(output, @"^(.+?)\((\d+),(\d+)\): Related location: (.+)$", RegexOptions.Multiline);
        if (primary.Count != errors || related.Count != errors || output.Split("Error:").Length - 1 != errors)
            return Inconclusive();
        var method = source.IndexOf("static method CloseStep(", StringComparison.Ordinal);
        if (method < 0)
            return Inconclusive();
        var start = source[..source.IndexOf('{', method)].Count(c => c == '\n') + 1;
        var end = source[..source.IndexOf("\n    }", method, StringComparison.Ordinal)].Count(c => c == '\n') + 2;
        var lines = source.Split('\n');
        var matched = new List<string>();
        foreach (Match p in primary)
            if (p.Groups[1].Value != "verification/Protocol.dfy" || !int.TryParse(p.Groups[2].Value, out var line) || line < start || line > end || p.Groups[4].Value != "a postcondition could not be proved on this return path")
                return Inconclusive();
        foreach (Match p in related)
        {
            if (p.Groups[1].Value != "verification/Protocol.dfy" || !int.TryParse(p.Groups[2].Value, out var line) || line < 1 || line > lines.Length || p.Groups[4].Value != "this is the postcondition that could not be proved")
                return Inconclusive();
            var clause = lines[line - 1].Trim();
            if (!mutant.AllowedClauses.Contains(clause))
                return Inconclusive();
            matched.Add(clause);
        }
        return new(matched.Contains(mutant.TargetClause) ? "detected" : "inconclusive", matched.ToArray());
    }
    public delegate Task<ExecutionResult> Executor(string[] command, string tree, string directory, double budget, CancellationToken cancellation);
    public static async Task<JsonObject> Suite(MutationCatalog catalog, Dictionary<string, string> sources, string output, string[] command, double baselineBudget, double mutantBudget, CancellationToken cancellation = default, Executor? executor = null)
    {
        executor ??= (c, t, d, b, ct) => Processes.Run(c, t, d, b, ct);
        var experiments = new JsonArray(catalog.Mutants.Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["status"] = "not-run" }).ToArray());
        var report = new JsonObject { ["schema_version"] = 1, ["status"] = "failed", ["source_hashes"] = Node(sources.ToDictionary(p => p.Key, p => Hash(Encoding.UTF8.GetBytes(p.Value)))), ["experiments"] = experiments };
        void SaveReport() => Save(Path.Combine(output, "results.json"), report);
        SaveReport();
        try
        {
            foreach (var mutant in new Mutant?[] { null }.Concat(catalog.Mutants))
            {
                var name = mutant?.Id ?? "baseline";
                var experiment = mutant is null ? new JsonObject { ["id"] = name, ["status"] = "not-run" } : experiments.Single(e => e!["id"]!.ToString() == name)!.AsObject();
                if (mutant is null)
                    experiments.Insert(0, experiment);
                else
                {
                    experiment["requirement"] = mutant.Requirement;
                    experiment["obligation"] = mutant.Obligation;
                    experiment["target_clause"] = mutant.TargetClause;
                }
                SaveReport();
                using (var tree = new TemporaryTree())
                {
                    foreach (var (file, source) in sources)
                    {
                        var path = Path.Combine(tree.Path, file);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllText(path, mutant is not null && file == mutant.File ? Apply(source, mutant) : source);
                    }
                    experiment["source_hashes"] = Node(sources.Keys.ToDictionary(f => f, f => Hash(File.ReadAllBytes(Path.Combine(tree.Path, f)))));
                    var record = await executor(command, tree.Path, Path.Combine(output, name), mutant is null ? baselineBudget : mutantBudget, cancellation);
                    foreach (var (key, value) in Node(record).AsObject())
                        experiment[key] = value?.DeepClone();
                    var outcome = Classify(record, Text(Path.Combine(tree.Path, "verification/Protocol.dfy")), mutant);
                    experiment["status"] = outcome.Status;
                    experiment["matched_clauses"] = Node(outcome.MatchedClauses);
                    Console.WriteLine($"{name}: {outcome.Status} ({record.ElapsedSeconds}s)");
                }
                SaveReport();
                if (experiment["status"]!.ToString() != (mutant is null ? "passed" : "detected"))
                    return report;
            }
            report["status"] = "passed";
            return report;
        }
        finally { SaveReport(); }
    }
    public async Task<int> Run(string output, double baselineBudget, double mutantBudget, int proofLimit, CancellationToken cancellation)
    {
        Fresh(output);
        try
        {
            Require(double.IsFinite(baselineBudget) && baselineBudget > 0 && double.IsFinite(mutantBudget) && mutantBudget > 0, "budgets must be positive and finite");
            Require(proofLimit > 0, "verification time limit must be positive");
            var catalog = LoadCatalog();
            var setup = await Processes.Run(["bash", Path.Combine(root, "scripts/dafny-toolchain.sh")], root, Path.Combine(output, "toolchain"), 180, cancellation);
            Require(setup.Success, "toolchain bootstrap failed");
            var tool = Path.Combine(setup.Stdout.Trim(), "dafny");
            var versions = new Dictionary<string, ExecutionResult>();
            foreach (var (name, executable, expected) in new[] { ("dafny", Path.Combine(tool, "dafny"), "4.11.0"), ("z3", Path.Combine(tool, "z3/bin/z3-4.12.1"), "4.12.1") })
            {
                var result = await Processes.Run([executable, "--version"], root, Path.Combine(output, name), 15, cancellation);
                versions[name] = result;
                Require(result.Success && result.Stdout.Contains(expected), $"{name}: unexpected/missing version");
            }
            Save(Path.Combine(output, "tool-versions.json"), versions);
            File.Copy(Path.Combine(root, "verification/mutations/close.json"), Path.Combine(output, "catalog.json"));
            File.Copy(Path.Combine(root, "verification/requirements.json"), Path.Combine(output, "requirements.json"));
            var sources = new[] { "verification/Masking.dfy", "verification/Protocol.dfy" }.ToDictionary(f => f, f => Text(Path.Combine(root, f)));
            foreach (var (file, source) in sources)
            {
                var path = Path.Combine(output, "sources", file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, source);
            }
            string[] command = [Path.Combine(tool, "dafny"), "verify", "verification/Masking.dfy", "--verify-included-files", "--allow-external-contracts", "--solver-path", Path.Combine(tool, "z3/bin/z3-4.12.1"), "--verification-time-limit", proofLimit.ToString(), "--cores", "2"];
            var report = await Suite(catalog, sources, output, command, baselineBudget, mutantBudget, cancellation);
            return report["status"]!.ToString() == "passed" ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Save(Path.Combine(output, "failure.json"), new { status = "inconclusive", error = error.Message }); Console.Error.WriteLine($"mutations: {error.Message}"); return 1; }
        finally { Console.WriteLine($"Evidence: {output}"); }
    }
}
