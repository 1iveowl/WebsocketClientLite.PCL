using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pdd;
using Xunit;

namespace Pdd.Tests;

// Fixture records name a Dafny line as {Module.Class.Member+N} or {Module.Class.Member-N}:
// N lines after or before the line of that member's body's opening brace, found with the
// declaration lookup and the declaration kinds the classifier uses, in the source the record
// is classified against. A line added above a declaration moves the fixture lines with it
// (#136 review finding 7, 2026-11-11). Rendering against the classified source, the mutated
// one for an observed mutant run, is the #145 review's finding 4 (2026-11-12).
public static class Fixtures
{
    static readonly Dictionary<string, string> Files = new() { ["ProtocolCore"] = "verification/Protocol.dfy", ["MaskingCore"] = "verification/Masking.dfy" };
    const string Kinds = "(?:" + Mutations.MethodKinds + "|" + Mutations.FunctionKinds + ")";
    static readonly ConcurrentDictionary<(string Source, string Symbol), int> BodyLines = new();
    static readonly Lazy<IReadOnlyDictionary<string, string>> Disk = new(() => Files.Values.ToDictionary(f => f, f => Data.Text(Path.Combine(RequirementsTests.Root, f))));
    // The Dafny sources as they are in the repository, read once.
    public static IReadOnlyDictionary<string, string> Sources => Disk.Value;
    // The 1-based line of the opening brace of the body of a method, function or predicate.
    public static int BodyLine(string source, string symbol) => BodyLines.GetOrAdd((source, symbol), key =>
    {
        var masked = Requirements.Masked(key.Source);
        var member = Requirements.DafnyMember(masked, key.Symbol, Kinds);
        Assert.True(member is { Opening: >= 0 }, $"no declaration body: {key.Symbol}");
        return Requirements.LineAt(masked, member!.Value.Opening);
    });
    // The 1-based line of the only line of `source` that contains `text`, or equals it when
    // `exact`, asserted to exist. With `after`, the first such line after the first line that
    // contains `after`.
    public static int LineOf(string source, string text, bool exact = false, string? after = null)
    {
        var lines = source.Split('\n');
        bool Matches(string line) => exact ? line == text : line.Contains(text, StringComparison.Ordinal);
        var start = after is null ? 0 : Array.FindIndex(lines, l => l.Contains(after, StringComparison.Ordinal));
        Assert.True(start >= 0, $"line not found: {after}");
        var index = Array.FindIndex(lines, start, Matches);
        Assert.True(index >= 0, $"line not found: {text}");
        if (after is null)
            Assert.True(index == Array.FindLastIndex(lines, Matches), $"line not unique: {text}");
        return index + 1;
    }
    public static string Lines(string text, IReadOnlyDictionary<string, string> sources) => Regex.Replace(text, @"\{(\w+\.\w+\.\w+)([+-]\d+)\}", m =>
    {
        var symbol = m.Groups[1].Value;
        return (BodyLine(sources[Files[symbol.Split('.')[0]]], symbol) + int.Parse(m.Groups[2].Value)).ToString();
    });
    // A copy of a fixture record with every anchor in its strings replaced by the line number
    // in `sources` (file to text), the sources the record is classified against.
    public static JsonNode Rendered(JsonNode node, IReadOnlyDictionary<string, string> sources) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Rendered(p.Value, sources)))),
        JsonArray a => new JsonArray(a.Select(v => v is null ? null : Rendered(v, sources)).ToArray()),
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Lines(s, sources))!,
        _ => node.DeepClone()
    };
}
