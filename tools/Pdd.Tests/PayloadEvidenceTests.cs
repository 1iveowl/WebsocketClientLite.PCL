using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class PayloadEvidenceTests
{
    static string Root => RequirementsTests.Root;
    static MutationCatalog Payload => new Mutations(Root).LoadCatalog(scope: "payload");
    // The observed records, each rendered once against the mutated source it is classified
    // against (#145 review, findings 4 and 6, 2026-11-12).
    static readonly Lazy<JsonArray> RenderedDiagnostics = new(() =>
    {
        var catalog = Payload;
        return new JsonArray(Data.Read(Path.Combine(Root, "verification/tests/fixtures/payload-diagnostics.json")).AsArray().Select(record =>
        {
            var mutant = catalog.Mutants.Single(m => m.Id == record!["id"]!.ToString());
            var sources = Fixtures.Sources.ToDictionary(p => p.Key, p => p.Key == mutant.File ? Mutations.Apply(p.Value, mutant) : p.Value);
            return (JsonNode?)Fixtures.Rendered(record!, sources);
        }).ToArray());
    });
    static JsonArray Diagnostics => RenderedDiagnostics.Value;
    public static IEnumerable<object[]> Cases() => Diagnostics.Select(c => new object[] { c!.ToJsonString() });
    [Theory, MemberData(nameof(Cases))]
    public void ObservedDiagnosticsRequireTheExactSemanticFailure(string json)
    {
        var fixture = Data.Parse(json);
        var mutant = Payload.Mutants.Single(m => m.Id == fixture["id"]!.ToString());
        var source = Mutations.Apply(Data.Text(Path.Combine(Root, mutant.File)), mutant);
        var record = Data.Model<ExecutionResult>(fixture);
        Assert.Equal("detected", Mutations.Classify(record, source, mutant).Status);
        Assert.Contains(mutant.TargetClause, Mutations.Classify(record, source, mutant).MatchedClauses);
        foreach (var mode in new[] { "wrong-file", "parse", "timeout", "interrupt", "launch", "unrelated-error", "wrong-clause" })
        {
            var invalid = Data.Model<ExecutionResult>(fixture);
            var target = mutant;
            switch (mode)
            {
                case "wrong-file": invalid.Stdout = invalid.Stdout.Replace(mutant.File, "verification/Unrelated.dfy"); break;
                case "parse": invalid.ExitCode = 2; break;
                case "timeout": invalid.TimedOut = true; break;
                case "interrupt": invalid.Interrupted = true; break;
                case "launch": invalid.LaunchError = "tool unavailable"; break;
                case "unrelated-error": invalid.Stdout += "\nError: unrelated failure\n"; break;
                case "wrong-clause": target = mutant with { TargetClause = "ensures false" }; break;
            }
            Assert.Equal("inconclusive", Mutations.Classify(invalid, source, target).Status);
        }
    }
    [Theory]
    [InlineData("signature")]
    [InlineData("Known")]
    [InlineData("PrefixValid")]
    [InlineData("without-postcondition")]
    public void PropositionLocationMustLieInAPredicateTheTargetNames(string mode)
    {
        var fixture = Diagnostics.Single(d => d!["id"]!.ToString() == "HEADER-001-short-sixteen-bit")!;
        var mutant = Payload.Mutants.Single(m => m.Id == "HEADER-001-short-sixteen-bit");
        var source = Mutations.Apply(Data.Text(Path.Combine(Root, mutant.File)), mutant);
        Assert.Equal("detected", Mutations.Classify(Data.Model<ExecutionResult>(fixture), source, mutant).Status);
        var inside = Regex.Match(fixture["stdout"]!.ToString(), @"Protocol\.dfy\((\d+),\d+\): Related location: this proposition").Groups[1].Value;
        Assert.Equal(Fixtures.LineOf(source, "else if marker == 126 then 126 <= length <= 65535 else length >= 65536) &&"), int.Parse(inside));
        var moved = Data.Model<ExecutionResult>(fixture);
        // Outside every function, and inside the bodies of two predicates the target clause
        // does not name: Known's body is on its declaration line, PrefixValid's body follows it.
        int? line = mode switch
        {
            "signature" => Fixtures.LineOf(source, "static method DecodeHeader("),
            "Known" => Fixtures.LineOf(source, "static function Known(op: bv8): bool { op == 0 ||"),
            "PrefixValid" => Fixtures.LineOf(source, "static function PrefixValid(first: bv8, second: bv8): bool {") + 2,
            _ => null
        };
        if (line is { } target)
        {
            Assert.DoesNotContain("PrefixValid(", mutant.TargetClause);
            Assert.DoesNotContain("Known(", mutant.TargetClause);
            if (mode == "PrefixValid")
                Assert.Contains("Known(op)", source.Split('\n')[target - 1]);
            moved.Stdout = moved.Stdout.Replace($"Protocol.dfy({inside},", $"Protocol.dfy({target},", StringComparison.Ordinal);
            Assert.Contains($"Protocol.dfy({target},", moved.Stdout);
        }
        else
            moved.Stdout = Regex.Replace(moved.Stdout, @"^.*Related location: this is the postcondition that could not be proved\r?\n", "", RegexOptions.Multiline);
        Assert.NotEqual(fixture["stdout"]!.ToString(), moved.Stdout);
        Assert.Equal("inconclusive", Mutations.Classify(moved, source, mutant).Status);
    }
    [Fact]
    public void ScopedOffsetEditDoesNotMutateProductionAndScalarTogether()
    {
        var source = Data.Text(Path.Combine(Root, "verification/Protocol.dfy"));
        var mutant = Payload.Mutants.Single(m => m.Id == "REASSEMBLY-002-offset-stall");
        var changed = Mutations.Apply(source, mutant);
        var scalar = source.IndexOf("static method MergeScalar", StringComparison.Ordinal);
        Assert.StartsWith(source[..scalar], changed);
        Assert.Equal(2, source.Split(mutant.Before).Length - 1);
        Assert.Equal(1, changed.Split(mutant.Before).Length - 1);
        Assert.Throws<InvalidDataException>(() => Mutations.Apply(source, mutant with { Before = mutant.TargetClause, After = "invariant true" }));
    }
    [Fact]
    public void CatalogRequiresCompletePayloadMappingAndKnownScope()
    {
        var catalog = new Mutations(Root).LoadCatalog();
        Assert.Equal(17, catalog.Mutants.Length);
        var data = Data.Read(Path.Combine(Root, "verification/mutations/payload.json"));
        data["mutants"]![0]!["contract_clause"] = "ensures true";
        Assert.Throws<InvalidDataException>(() => new Mutations(Root).LoadCatalog(data, "payload"));
        Assert.Throws<InvalidDataException>(() => new Mutations(Root).LoadCatalog(scope: "unknown"));
        data = Data.Read(Path.Combine(Root, "verification/mutations/payload.json"));
        data["mutants"]![0]!["symbol"] = "ProtocolCore.Protocol.Unmapped";
        Assert.Throws<InvalidDataException>(() => new Mutations(Root).LoadCatalog(data, "payload"));
        data = Data.Read(Path.Combine(Root, "verification/mutations/payload.json"));
        foreach (var entry in data["mutants"]!.AsArray().Where(m => m!["requirement"]!.ToString() == "MASK-003").ToArray())
            data["mutants"]!.AsArray().Remove(entry);
        Assert.Throws<InvalidDataException>(() => new Mutations(Root).LoadCatalog(data, "payload"));
    }
    [Fact]
    public void ResolverReadsAttributesMultilineClausesAndNativeExpressionBody()
    {
        var manifest = new Requirements(Root).Validate();
        Assert.Contains("modifies data", manifest.Requirements.Single(r => r.Id == "MASK-001").Contracts.Single().Clauses);
        var copy = manifest.Requirements.Single(r => r.Id == "MASK-002").Contracts.Single();
        Assert.Contains(copy.Clauses.Single(c => c.StartsWith("ensures forall j :: 0 <= j < count")), Requirements.Clauses(Requirements.Resolve(Root, copy)));
        var native = manifest.Requirements.Single(r => r.Id == "REASSEMBLY-003");
        Assert.Contains("CopySlice", Requirements.Resolve(Root, native.ProductionPaths.Single().Reference));
        Assert.Empty(native.Contracts);
        Assert.DoesNotContain(native.Obligations, o => o.Kind is "proof" or "mutation");
        Assert.DoesNotContain("NATIVE-COPY", manifest.Requirements.Single(r => r.Id == "REASSEMBLY-002").Assumptions);
    }
    [Fact]
    public void LegacyCloseEvidenceCannotPassNewPayloadObligations()
    {
        using var tree = new TemporaryTree();
        var fixtures = Path.Combine(Root, "verification/tests/fixtures");
        ReceiptTests.CopyTree(Path.Combine(fixtures, "evidence"), tree.Path);
        foreach (var job in new[] { "protocol", "tests" })
        {
            var path = Path.Combine(tree.Path, job, "job.json");
            var data = Data.Read(path); data["ci"] = Data.Node(Evidence.CiIdentity()); Data.Save(path, data);
        }
        var result = new Evidence(Root).Aggregate(tree.Path, Data.Model<SourceIdentity>(Data.Read(Path.Combine(fixtures, "identity.json"))));
        Assert.Equal("failed", result["gate"]!["status"]!.ToString());
        var payload = result["requirements"]!.AsArray().Where(r => r!["id"]!.ToString().StartsWith("REASSEMBLY-", StringComparison.Ordinal) || r["id"]!.ToString().StartsWith("MASK-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, payload.Length);
        Assert.All(payload, r =>
        {
            Assert.False(r!["satisfied"]!.GetValue<bool>());
            Assert.All(r["machine_evidence"]!.AsArray().Where(o => o!["kind"]!.ToString() is "test" or "mutation"), o => Assert.NotEqual("passed", o!["status"]!.ToString()));
        });
    }
    [Fact]
    public async Task PayloadSuiteEditsOneTemporarySourceAtATime()
    {
        using var output = new TemporaryTree();
        var sources = new[] { "verification/Protocol.dfy", "verification/Masking.dfy" }.ToDictionary(f => f, f => Data.Text(Path.Combine(Root, f)));
        var trees = new List<string>();
        var catalog = Payload;
        Task<ExecutionResult> Execute(string[] command, string tree, string directory, double budget, CancellationToken token)
        {
            trees.Add(tree);
            var mutant = trees.Count == 1 ? null : catalog.Mutants[trees.Count - 2];
            foreach (var (file, original) in sources)
                Assert.Equal(mutant?.File == file ? Mutations.Apply(original, mutant) : original, Data.Text(Path.Combine(tree, file)));
            return Task.FromResult(mutant is null ? new ExecutionResult { ExitCode = 0, Stdout = "Dafny program verifier finished with 574 verified, 0 errors" } : Data.Model<ExecutionResult>(Diagnostics.Single(d => d!["id"]!.ToString() == mutant.Id)!));
        }
        var result = await Mutations.Suite(catalog, sources, output.Path, ["fixture"], 1, 1, executor: Execute);
        Assert.Equal("passed", result["status"]!.ToString());
        Assert.Equal(13, trees.Count);
        Assert.All(trees, tree => Assert.False(Directory.Exists(tree)));
        Assert.All(sources, s => Assert.Equal(s.Value, Data.Text(Path.Combine(Root, s.Key))));
    }
}
