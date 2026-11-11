using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Pdd.Data;

namespace Pdd;

public sealed class Mutations(string root)
{
    static readonly Dictionary<string, string> Scopes = new()
    {
        ["ProtocolCore.Protocol.CloseStep"] = "verification/Protocol.dfy",
        ["ProtocolCore.Protocol.DecodeHeader"] = "verification/Protocol.dfy",
        ["ProtocolCore.Protocol.Merge"] = "verification/Protocol.dfy",
        ["ProtocolCore.Protocol.MergeScalar"] = "verification/Protocol.dfy",
        ["ProtocolCore.Protocol.CopyMasked"] = "verification/Protocol.dfy",
        ["ProtocolCore.Protocol.MaskSlice"] = "verification/Protocol.dfy",
        ["MaskingCore.Masking.Apply"] = "verification/Masking.dfy"
    };
    public MutationCatalog LoadCatalog(JsonNode? data = null, string scope = "all")
    {
        Require(scope is "all" or "close" or "payload", "unsupported mutation suite");
        if (data is null)
        {
            var entries = new JsonArray();
            foreach (var name in scope == "all" ? new[] { "close", "payload" } : new[] { scope })
            {
                var file = Read(Path.Combine(root, $"verification/mutations/{name}.json"));
                Require(file["schema_version"]?.GetValue<int>() == 1 && file.AsObject().Select(p => p.Key).ToHashSet().SetEquals(["schema_version", "mutants"]), "unsupported catalog shape/version");
                foreach (var item in file["mutants"]!.AsArray()) entries.Add(item!.DeepClone());
            }
            data = new JsonObject { ["schema_version"] = 1, ["mutants"] = entries };
        }
        Require(data is JsonObject && data.AsObject().Select(p => p.Key).ToHashSet().SetEquals(["schema_version", "mutants"]) && data["schema_version"]?.GetValue<int>() == 1, "unsupported catalog shape/version");
        var manifest = new Requirements(root).Validate();
        var ids = new HashSet<string>();
        Require(data["mutants"] is JsonArray { Count: > 0 }, "empty catalog");
        string[] fields = ["id", "requirement", "obligation", "file", "symbol", "before", "after", "target_clause", "allowed_clauses", "classification"];
        foreach (var item in data["mutants"]!.AsArray())
        {
            Require(item is JsonObject && fields.All(item.AsObject().ContainsKey) && item.AsObject().All(p => fields.Contains(p.Key) || p.Key == "contract_clause"), "invalid mutant fields");
            Require(fields.Where(k => k != "allowed_clauses").All(k => item![k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0), "empty mutant field");
            Require(item!["allowed_clauses"] is JsonArray a && a.All(c => c is JsonValue v && v.TryGetValue<string>(out _)), "missing target clause");
        }
        var catalog = Model<MutationCatalog>(data);
        bool Selected(string id) => scope == "all" || id.StartsWith("CLOSE-", StringComparison.Ordinal) == (scope == "close");
        foreach (var m in catalog.Mutants)
        {
            Require(Regex.IsMatch(m.Id, @"^(CLOSE|REASSEMBLY|MASK|HEADER)-[0-9]{3}-[a-z-]+$") && ids.Add(m.Id), "invalid/duplicate mutant ID");
            Require(Selected(m.Requirement), "mutant outside selected suite");
            Require(Scopes.TryGetValue(m.Symbol, out var file) && file == m.File, "unsupported mutation scope");
            var req = manifest.Requirements.SingleOrDefault(r => r.Id == m.Requirement);
            Require(req is not null && req.Obligations.Any(o => o.Id == m.Obligation && o.Kind == "mutation"), "dangling mutation obligation");
            Require(req!.Contracts.Any(c => c.File == m.File && c.Symbol == m.Symbol && c.Clauses.Contains(m.ContractClause ?? m.TargetClause)), "target not mapped to requirement");
            var header = Requirements.Clauses(Requirements.Resolve(root, m));
            var source = Requirements.Source(root, m.File);
            var (start, end) = Bounds(source, m);
            var body = Requirements.Clauses(source[(start + 1)..end], "invariant");
            Require(m.AllowedClauses.Contains(m.TargetClause), "missing target clause");
            Require(m.Classification switch
            {
                "postcondition" => m.ContractClause is null && m.AllowedClauses.All(c => c.StartsWith("ensures ") && header.Contains(c)),
                "invariant" => m.ContractClause is not null && header.Contains(m.ContractClause) && m.AllowedClauses.All(c => c.StartsWith("invariant ") && body.Contains(c)),
                "frame" => m.ContractClause is null && m.AllowedClauses.Length == 1 && m.TargetClause.StartsWith("modifies ") && header.Contains(m.TargetClause),
                _ => false
            }, "unresolved allowed clause or unsupported classification");
            Apply(source, m);
        }
        foreach (var o in manifest.Requirements.Where(r => Selected(r.Id)).SelectMany(r => r.Obligations).Where(o => o.Kind == "mutation" && o.Delivery is "B" or "existing"))
            Require(catalog.Mutants.Any(m => m.Obligation == o.Id), "uncovered mutation obligation");
        return catalog;
    }
    static (int Start, int End) Bounds(string source, Mutant m)
    {
        Require(Scopes.TryGetValue(m.Symbol, out var file) && file == m.File, "unsupported mutation scope");
        var masked = Requirements.Masked(source);
        var declarations = Regex.Matches(masked, @"\bstatic\s+method\s+(?:\{:[^}]*\}\s*)?" + Regex.Escape(m.Symbol.Split('.').Last()) + @"\s*\(");
        Require(declarations.Count == 1, "missing or ambiguous mutation method");
        var start = masked.IndexOf('{', declarations[0].Index + declarations[0].Length);
        var body = Requirements.Body(masked, start);
        return (start, start + 1 + body.Length);
    }
    public static string Apply(string source, Mutant m)
    {
        var (start, end) = Bounds(source, m);
        var body = source[(start + 1)..end];
        Require(!(m.Before + m.After).Contains('\n') && m.Before != m.After && body.Split(m.Before).Length == 2, "mutation must be a unique, changed single line in its method");
        Require(!m.After.Any(c => c is '{' or '}') && !Regex.IsMatch(m.Before + "\n" + m.After, @"\b(requires|ensures|invariant|modifies|assert|assume)\b"), "mutation must change implementation, not proof clauses");
        return source[..(start + 1)] + body.Replace(m.Before, m.After) + source[end..];
    }
    public static MutationOutcome Classify(ExecutionResult record, string source, Mutant? mutant = null)
    {
        MutationOutcome Inconclusive() => new("inconclusive", []);
        if (record.TimedOut || record.Interrupted || record.LaunchError is not null) return Inconclusive();
        var output = record.Stdout + "\n" + record.Stderr;
        var summaries = Regex.Matches(output, @"Dafny program verifier finished with (\d+) verified, (\d+) errors?");
        if (summaries.Count != 1 || Regex.IsMatch(output, @"timed out|timeout|resource limit|inconclusive", RegexOptions.IgnoreCase)) return Inconclusive();
        var verified = int.Parse(summaries[0].Groups[1].Value);
        var errors = int.Parse(summaries[0].Groups[2].Value);
        if (record.ExitCode == 0 && errors == 0 && verified > 0 && !output.Contains("Error:")) return new(mutant is null ? "passed" : "survived", []);
        if (mutant is null || record.ExitCode != 4 || errors == 0 || !mutant.AllowedClauses.Contains(mutant.TargetClause)) return Inconclusive();
        var primary = Regex.Matches(output, @"^(.+?)\((\d+),(\d+)\): Error: (.+)$", RegexOptions.Multiline);
        var related = Regex.Matches(output, @"^(.+?)\((\d+),(\d+)\): Related location: (.+)$", RegexOptions.Multiline);
        if (primary.Count != errors || output.Split("Error:").Length - 1 != errors) return Inconclusive();
        int start, end;
        try { (start, end) = Bounds(source, mutant); } catch (InvalidDataException) { return Inconclusive(); }
        var firstLine = source[..start].Count(c => c == '\n') + 1;
        var lastLine = source[..end].Count(c => c == '\n') + 1;
        var lines = source.Split('\n');
        var matched = new List<string>();
        foreach (Match p in primary)
        {
            if (p.Groups[1].Value != mutant.File || !int.TryParse(p.Groups[2].Value, out var line) || line < firstLine || line > lastLine) return Inconclusive();
            var message = p.Groups[4].Value;
            if (mutant.Classification == "postcondition")
            {
                if (message != "a postcondition could not be proved on this return path") return Inconclusive();
            }
            else if (mutant.Classification == "invariant")
            {
                if (message != "this invariant could not be proved to be maintained by the loop") return Inconclusive();
                var clause = Requirements.LogicalLine(lines, line - 1);
                if (!mutant.AllowedClauses.Contains(clause)) return Inconclusive();
                matched.Add(clause);
            }
            else if (mutant.Classification == "frame")
            {
                if (message != "assignment might update an array element not in the enclosing context's modifies clause" || !lines[line - 1].Contains(mutant.After)) return Inconclusive();
                matched.Add(mutant.TargetClause);
            }
            else return Inconclusive();
        }
        if (mutant.Classification == "postcondition")
        {
            // Dafny may add "this proposition could not be proved" inside a predicate the
            // failed clause calls. Accept it only inside the body of a predicate named in
            // the target clause; every error still needs its own postcondition location.
            const string Postcondition = "this is the postcondition that could not be proved";
            if (related.Count(p => p.Groups[4].Value == Postcondition) != errors) return Inconclusive();
            List<(int First, int Last)>? predicates = null;
            foreach (Match p in related)
            {
                if (p.Groups[1].Value != mutant.File || !int.TryParse(p.Groups[2].Value, out var line) || line < 1 || line > lines.Length) return Inconclusive();
                if (p.Groups[4].Value == "this proposition could not be proved")
                {
                    predicates ??= NamedPredicateLines(source, mutant.TargetClause);
                    if (!predicates.Any(r => line >= r.First && line <= r.Last)) return Inconclusive();
                    continue;
                }
                if (p.Groups[4].Value != Postcondition) return Inconclusive();
                var clause = Requirements.LogicalLine(lines, line - 1);
                if (!mutant.AllowedClauses.Contains(clause)) return Inconclusive();
                matched.Add(clause);
            }
        }
        else if (related.Count != 0) return Inconclusive();
        if (mutant.Classification == "invariant" && Regex.Matches(output, @"^\s*Related message: loop invariant violation\s*$", RegexOptions.Multiline).Count != errors) return Inconclusive();
        return new(matched.Contains(mutant.TargetClause) ? "detected" : "inconclusive", matched.ToArray());
    }
    // The line ranges of the bodies of the functions and predicates the clause names,
    // found once per classification rather than once per related location.
    static List<(int First, int Last)> NamedPredicateLines(string source, string clause)
    {
        var masked = Requirements.Masked(source);
        var ranges = new List<(int First, int Last)>();
        foreach (Match d in Regex.Matches(masked, @"\b(?:ghost\s+)?(?:function|predicate)\s+(\w+)\s*\("))
        {
            if (!Regex.IsMatch(clause, @"\b" + Regex.Escape(d.Groups[1].Value) + @"\s*\(")) continue;
            var open = masked.IndexOf('{', d.Index + d.Length);
            if (open < 0) continue;
            var first = masked[..open].Count(c => c == '\n') + 1;
            ranges.Add((first, first + Requirements.Body(masked, open).Count(c => c == '\n')));
        }
        return ranges;
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
                    experiment["classification"] = mutant.Classification;
                    experiment["contract_clause"] = mutant.ContractClause ?? mutant.TargetClause;
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
                    var outcome = Classify(record, Text(Path.Combine(tree.Path, mutant?.File ?? "verification/Protocol.dfy")), mutant);
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
    public async Task<int> Run(string output, double baselineBudget, double mutantBudget, int proofLimit, CancellationToken cancellation, string scope = "all")
    {
        Fresh(output);
        try
        {
            Require(double.IsFinite(baselineBudget) && baselineBudget > 0 && double.IsFinite(mutantBudget) && mutantBudget > 0, "budgets must be positive and finite");
            Require(proofLimit > 0, "verification time limit must be positive");
            var catalog = LoadCatalog(scope: scope);
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
            Save(Path.Combine(output, "catalog.json"), catalog);
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
