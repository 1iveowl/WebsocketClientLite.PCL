using System.Text.Json.Nodes;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class MutationTests
{
    static string Root => RequirementsTests.Root;
    static string Source => Data.Text(Path.Combine(Root, "verification/Protocol.dfy"));
    static MutationCatalog Catalog => new Mutations(Root).LoadCatalog();
    static Dictionary<string, string> Sources => new[] { "verification/Protocol.dfy", "verification/Masking.dfy" }.ToDictionary(f => f, f => Data.Text(Path.Combine(Root, f)));
    public static IEnumerable<object[]> Cases() => Data.Read(Path.Combine(Root, "verification/tests/fixtures/mutation-cases.json")).AsArray().Select(c => new object[] { c!.ToJsonString() });
    [Theory, MemberData(nameof(Cases))]
    public void PythonDiagnosticFixturesMatch(string json)
    {
        var fixture = Data.Parse(json);
        var outcome = Mutations.Classify(Data.Model<ExecutionResult>(fixture["record"]!), Source, fixture["mutant"]!.GetValue<bool>() ? Catalog.Mutants[0] : null);
        Assert.Equal(fixture["expected"]![0]!.ToString(), outcome.Status);
        Assert.Equal(fixture["expected"]![1]!.AsArray().Select(c => c!.ToString()), outcome.MatchedClauses);
    }
    [Fact]
    public void ExactEditRejectsMissingDuplicateAndContractEdits()
    {
        var mutant = Catalog.Mutants[0];
        foreach (var source in new[] { Source.Replace(mutant.Before, ""), Source + mutant.Before })
            Assert.Throws<InvalidDataException>(() => Mutations.Apply(source, mutant));
        Assert.Throws<InvalidDataException>(() => Mutations.Apply(Source, mutant with { Before = mutant.TargetClause, After = "ensures true" }));
    }
    [Theory]
    [InlineData("dangling")]
    [InlineData("uncovered")]
    [InlineData("duplicate")]
    [InlineData("target")]
    public void CatalogRejectsInvalidEntries(string mode)
    {
        var data = Data.Read(Path.Combine(Root, "verification/mutations/close.json"));
        var mutants = data["mutants"]!.AsArray();
        switch (mode)
        {
            case "dangling":
                mutants[0]!["obligation"] = "MISSING";
                break;
            case "uncovered":
                while (mutants.Count > 2)
                    mutants.RemoveAt(2);
                break;
            case "duplicate":
                mutants.Add(mutants[0]!.DeepClone());
                break;
            case "target":
                mutants[0]!["target_clause"] = "ensures true";
                break;
        }
        Assert.Throws<InvalidDataException>(() => new Mutations(Root).LoadCatalog(data));
    }
    static ExecutionResult Passed() => new() { ExitCode = 0, Stdout = "Dafny program verifier finished with 41 verified, 0 errors", ElapsedSeconds = .1 };
    static ExecutionResult Detected(Mutant mutant)
    {
        var line = Array.IndexOf(Source.Split('\n'), "      " + mutant.TargetClause) + 1;
        return new()
        {
            ExitCode = 4,
            Stdout = $"verification/Protocol.dfy(153,4): Error: a postcondition could not be proved on this return path\nverification/Protocol.dfy({line},22): Related location: this is the postcondition that could not be proved\nDafny program verifier finished with 40 verified, 1 error\n",
            ElapsedSeconds = .1
        };
    }
    [Theory]
    [InlineData("baseline-failure")]
    [InlineData("survivor")]
    [InlineData("interrupted")]
    [InlineData("success")]
    public async Task SuiteIsolationAndPartialEvidence(string mode)
    {
        using var output = new TemporaryTree();
        var trees = new List<string>();
        var catalog = Catalog;
        var sources = Sources;
        Task<ExecutionResult> Execute(string[] command, string tree, string directory, double budget, CancellationToken token)
        {
            trees.Add(tree);
            Assert.NotEqual(Root, tree);
            var actual = Data.Text(Path.Combine(tree, "verification/Protocol.dfy"));
            Assert.Equal(trees.Count == 1 ? Source : Mutations.Apply(Source, catalog.Mutants[trees.Count - 2]), actual);
            Assert.Equal(sources["verification/Masking.dfy"], Data.Text(Path.Combine(tree, "verification/Masking.dfy")));
            if (mode == "interrupted" && trees.Count == 2)
                throw new OperationCanceledException();
            var record = mode == "success" && trees.Count > 1 ? Detected(catalog.Mutants[trees.Count - 2]) : Passed();
            if (mode == "baseline-failure")
                record.ExitCode = 2;
            return Task.FromResult(record);
        }
        if (mode == "interrupted")
            await Assert.ThrowsAsync<OperationCanceledException>(() => Mutations.Suite(catalog, sources, output.Path, ["unused"], 1, 1, executor: Execute));
        else
        {
            var report = await Mutations.Suite(catalog, sources, output.Path, ["unused"], 1, 1, executor: Execute);
            Assert.Equal(mode == "success" ? "passed" : "failed", report["status"]!.ToString());
            Assert.Equal(mode == "success" ? 6 : mode == "baseline-failure" ? 1 : 2, trees.Count);
        }
        Assert.True(File.Exists(Path.Combine(output.Path, "results.json")));
        Assert.All(trees, t => Assert.False(Directory.Exists(t)));
        Assert.Equal(Source, Data.Text(Path.Combine(Root, "verification/Protocol.dfy")));
        Assert.Equal(trees.Count, trees.Distinct().Count());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidProofBudgetFailsBeforeBootstrap(int limit)
    {
        using var tree = new TemporaryTree();
        var output = Path.Combine(tree.Path, "evidence");
        Assert.Equal(1, await new Mutations(Root).Run(output, 180, 90, limit, default));
        Assert.Contains("verification time limit must be positive", Data.Read(Path.Combine(output, "failure.json"))["error"]!.ToString());
        Assert.False(Directory.Exists(Path.Combine(output, "toolchain")));
    }
}
