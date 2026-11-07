using System.Text.Json.Nodes;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class ReceiptTests
{
    static string Fixtures => Path.Combine(RequirementsTests.Root, "verification/tests/fixtures");
    static SourceIdentity Identity => Data.Model<SourceIdentity>(Data.Read(Path.Combine(Fixtures, "identity.json")));
    internal static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
    static JsonObject Projection(JsonObject receipt)
    {
        var requirements = new JsonArray();
        foreach (var q in receipt["requirements"]!.AsArray())
        {
            var obligations = new JsonObject();
            foreach (var o in q!["machine_evidence"]!.AsArray())
                obligations[o!["id"]!.ToString()] = o["status"]!.DeepClone();
            requirements.Add(new JsonObject { ["id"] = q["id"]!.DeepClone(), ["satisfied"] = q["satisfied"]!.DeepClone(), ["obligations"] = obligations });
        }
        var reviews = new JsonObject();
        foreach (var r in receipt["review_evidence"]!.AsArray())
            reviews[r!["id"]!.ToString()] = r["status"]!.DeepClone();
        var matrix = new JsonObject();
        foreach (var (key, value) in receipt["framework_matrix"]!.AsObject())
            matrix[key] = new JsonObject { ["status"] = value!["status"]!.DeepClone(), ["host_runtime"] = value["host_runtime"]?.DeepClone(), ["tests"] = value["tests"]!.DeepClone() };
        return new JsonObject { ["gate"] = receipt["gate"]!["status"]!.DeepClone(), ["requirements"] = requirements, ["reviews"] = reviews, ["stryker"] = receipt["stryker"]!["status"]!.DeepClone(), ["matrix"] = matrix, ["coverage"] = receipt["coverage"]!["status"]!.DeepClone(), ["accepted_jobs"] = Data.Node(receipt["jobs"]!.AsObject().Select(p => p.Key).Order().ToArray()), ["rejected_jobs"] = Data.Node(receipt["rejected_jobs"]!.AsObject().Select(p => p.Key).Order().ToArray()) };
    }
    static void BindCi(string directory)
    {
        foreach (var job in new[] { "protocol", "tests" })
        {
            var file = Path.Combine(directory, job, "job.json");
            var data = Data.Read(file);
            data["ci"] = Data.Node(Evidence.CiIdentity());
            Data.Save(file, data);
        }
    }
    public static IEnumerable<object[]> Cases() => Data.Read(Path.Combine(Fixtures, "receipt-cases.json")).AsArray().Select(c => new object[] { c!.ToJsonString() });
    [Fact]
    public void LegacySuccessFixtureNowRequiresEEvidence()
    {
        using var tree = new TemporaryTree();
        CopyTree(Path.Combine(Fixtures, "evidence"), tree.Path);
        BindCi(tree.Path);
        var actual = Projection(new Evidence(RequirementsTests.Root).Aggregate(tree.Path, Identity));
        var expected = Data.Read(Path.Combine(Fixtures, "receipt-success.expected.json"));
        expected["gate"] = "failed"; // E evidence is now mandatory; preserve the D fixture.
        Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
    }
    [Theory, MemberData(nameof(Cases))]
    public void PythonFailureFixturesMatch(string json)
    {
        using var tree = new TemporaryTree();
        CopyTree(Path.Combine(Fixtures, "evidence"), tree.Path);
        BindCi(tree.Path);
        var fixture = Data.Parse(json);
        var path = Path.Combine(tree.Path, fixture["path"]!.ToString());
        switch (fixture["operation"]!.ToString())
        {
            case "delete":
                File.Delete(path);
                break;
            case "text":
                File.WriteAllText(path, fixture["value"]!.ToString());
                break;
            default:
                var data = Data.Read(path);
                RequirementsTests.Edit(data, fixture["keys"]!.AsArray(), fixture["value"]);
                Data.Save(path, data);
                break;
        }
        var actual = Projection(new Evidence(RequirementsTests.Root).Aggregate(tree.Path, Identity));
        // D2 resolves a legacy inconsistency: a nonzero exit with status=passed
        // must not remain passed on the requirement's proof obligation.
        var expected = fixture["expected"]!;
        if (fixture["name"]!.ToString() == "proof-passed")
            foreach (var q in expected["requirements"]!.AsArray())
                foreach (var (key, value) in q!["obligations"]!.AsObject().ToArray())
                    if (key.EndsWith("-PROOF"))
                        q["obligations"]![key] = "failed";
        Assert.True(JsonNode.DeepEquals(expected, actual), fixture["name"] + "\n" + actual.ToJsonString());
    }
    [Theory]
    [InlineData("duplicate-json")]
    [InlineData("malformed-trx")]
    [InlineData("duplicate-mutation")]
    [InlineData("traversal")]
    [InlineData("timeout-summary")]
    [InlineData("missing-definition")]
    [InlineData("nan-coverage")]
    [InlineData("malformed-clauses")]
    [InlineData("interrupted-mutation")]
    [InlineData("malformed-versions")]
    [InlineData("wrong-version")]
    public void AdditionalFailClosedCases(string mode)
    {
        using var tree = new TemporaryTree();
        CopyTree(Path.Combine(Fixtures, "evidence"), tree.Path);
        BindCi(tree.Path);
        var job = Path.Combine(tree.Path, "protocol/job.json");
        var trx = Path.Combine(tree.Path, "tests/tests/net8.0/results.trx");
        switch (mode)
        {
            case "duplicate-json":
                File.WriteAllText(job, "{\"job\":\"protocol\",\"job\":\"protocol\"}");
                break;
            case "malformed-trx":
                File.WriteAllText(trx, "<");
                break;
            case "interrupted-mutation":
                var interruptedPath = Path.Combine(tree.Path, "protocol/mutations/results.json");
                var interrupted = Data.Read(interruptedPath);
                interrupted["experiments"]![1]!["interrupted"] = true;
                Data.Save(interruptedPath, interrupted);
                break;
            case "malformed-versions":
                File.WriteAllText(Path.Combine(tree.Path, "protocol/mutations/tool-versions.json"), "[]");
                break;
            case "wrong-version":
                File.WriteAllText(Path.Combine(tree.Path, "protocol/mutations/tool-versions.json"), "{\"dafny\":{\"stdout\":\"5.0.0\"},\"z3\":{\"stdout\":\"4.12.1\"}}");
                break;
            case "malformed-clauses":
                var malformedPath = Path.Combine(tree.Path, "protocol/mutations/results.json");
                var malformed = Data.Read(malformedPath);
                malformed["experiments"]![1]!["matched_clauses"] = "not an array";
                Data.Save(malformedPath, malformed);
                break;
            case "duplicate-mutation":
                var path = Path.Combine(tree.Path, "protocol/mutations/results.json");
                var data = Data.Read(path);
                data["experiments"]!.AsArray().Add(data["experiments"]![1]!.DeepClone());
                Data.Save(path, data);
                break;
            case "traversal":
                var record = Data.Read(job);
                record["runs"]!["proof"]!["log"] = "../../outside";
                Data.Save(job, record);
                break;
            case "timeout-summary":
                File.WriteAllText(Path.Combine(tree.Path, "protocol/proof.log"), "Dafny program verifier finished with 40 verified, 0 errors, 1 time out");
                break;
            case "missing-definition":
                File.WriteAllText(trx, File.ReadAllText(trx).Replace("testId=\"0\"", "testId=\"missing\""));
                break;
            case "nan-coverage":
                File.WriteAllText(Path.Combine(tree.Path, "tests/coverage/coverage.cobertura.xml"), "<coverage line-rate=\"NaN\"/>");
                break;
        }
        Assert.Equal("failed", new Evidence(RequirementsTests.Root).Aggregate(tree.Path, Identity)["gate"]!["status"]!.ToString());
    }
    [Fact]
    public async Task CaptureFailureSkipsFollowingAndRejectsStaleDirectory()
    {
        using var tree = new TemporaryTree();
        var output = Path.Combine(tree.Path, "capture");
        var evidence = new Evidence(RequirementsTests.Root);
        Assert.Equal(1, await evidence.Capture("tests", output, 10, commands: [("fail", ["bash", "-c", "exit 7"]), ("later", ["bash", "-c", "exit 0"])]));
        var report = Data.Read(Path.Combine(output, "job.json"));
        Assert.Equal(7, report["runs"]!["fail"]!["exit_code"]!.GetValue<int>());
        Assert.Equal("skipped", report["runs"]!["later"]!["status"]!.ToString());
        await Assert.ThrowsAsync<InvalidDataException>(() => evidence.Capture("tests", output, 10));
    }
}
