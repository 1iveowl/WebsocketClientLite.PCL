using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Pdd;

public record SourceReference(string File, string Symbol);
public record Contract(string File, string Symbol, string[] Clauses) : SourceReference(File, Symbol);
public record ProductionPath(SourceReference Reference, string Responsibility);
public record Obligation(string Id, string Kind, string Delivery, string Description, SourceReference[] References);
public record Requirement(string Id, string Intent, string[] Origin, string[] Exclusions, string[] Assumptions, Contract[] Contracts, ProductionPath[] ProductionPaths, Obligation[] Obligations);
public record RequirementManifest(int SchemaVersion, string BoundariesFile, Requirement[] Requirements);
public record Boundary(string Id, string Assumption, SourceReference[] Owners, string[] Requirements, string[] TestObligations, string[] Exclusions, string ReviewAnchor);
public record BoundaryRegister(int SchemaVersion, Boundary[] Boundaries);
public record Mutant(string Id, string Requirement, string Obligation, string File, string Symbol, string Before, string After, string TargetClause, string[] AllowedClauses, string Classification, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContractClause = null) : SourceReference(File, Symbol);
public record MutationCatalog(int SchemaVersion, Mutant[] Mutants);
public record MutationOutcome(string Status, string[] MatchedClauses);
public sealed class ExecutionResult
{
    public string[] Command { get; set; } = [];
    public double BudgetSeconds
    {
        get; set;
    }
    public string StartedAt { get; set; } = Data.Now();
    public int? ExitCode
    {
        get; set;
    }
    public bool TimedOut
    {
        get; set;
    }
    public bool Interrupted
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LaunchError
    {
        get; set;
    }
    public double ElapsedSeconds
    {
        get; set;
    }
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    [JsonIgnore] public bool Success => ExitCode == 0 && !TimedOut && !Interrupted && LaunchError is null;
}
public record SourceIdentity(string TestedCommit, string? PrSourceHead, bool DirtyWorktree, SortedDictionary<string, string?> SourceHashes);
public sealed class CommandReceipt
{
    public string[] Command { get; set; } = [];
    public string? StartedAt
    {
        get; set;
    }
    public string? FinishedAt
    {
        get; set;
    }
    public string Status { get; set; } = "not-run";
    public int? ExitCode
    {
        get; set;
    }
    public string Log { get; set; } = "output.log";
}
public sealed class JobReceipt
{
    public int SchemaVersion { get; set; } = 1;
    public required string Job
    {
        get; set;
    }
    public required SourceIdentity Source
    {
        get; set;
    }
    public SourceIdentity? SourceAfter
    {
        get; set;
    }
    public Dictionary<string, string?> Ci { get; set; } = Evidence.CiIdentity();
    public string StartedAt { get; set; } = Data.Now();
    public string? FinishedAt
    {
        get; set;
    }
    public Dictionary<string, JsonNode?> Tools { get; set; } = [];
    public Dictionary<string, CommandReceipt> Runs { get; set; } = [];
}
public static class Data
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string Now() => DateTimeOffset.UtcNow.ToString("O");
    public static JsonNode Node(object value) => JsonSerializer.SerializeToNode(value, Options)!;
    public static T Model<T>(JsonNode value) => value.Deserialize<T>(Options) ?? throw new InvalidDataException("Missing model");
    public static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
    public static JsonNode Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        void Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>();
                foreach (var p in element.EnumerateObject())
                {
                    Require(names.Add(p.Name), $"duplicate JSON key: {p.Name}");
                    Unique(p.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray())
                    Unique(item);
        }
        Unique(document.RootElement);
        return JsonNode.Parse(text) ?? throw new InvalidDataException("Null JSON document");
    }
    public static JsonNode Read(string path) => Parse(Text(path));
    public static void Save(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options) + "\n");
        File.Move(temporary, path, true);
    }
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    public static string Text(string path) => new System.Text.UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');
    public static void Fresh(string path)
    {
        Require(!Directory.Exists(path) && !File.Exists(path), "Evidence directory already exists: " + path);
        Directory.CreateDirectory(path);
    }
}
