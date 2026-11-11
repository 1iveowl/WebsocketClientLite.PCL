using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pdd;
using Xunit;

namespace Pdd.Tests;

// Fixture records name a Dafny line as {Module.Class.Member+N} or {Module.Class.Member-N}:
// N lines after or before the line of that member's body's opening brace, found in the
// current source with the declaration lookup the classifier uses. A line added above a
// declaration moves the fixture lines with it (#136 review finding 7, 2026-11-11).
public static class Fixtures
{
    static readonly Dictionary<string, string> Files = new() { ["ProtocolCore"] = "verification/Protocol.dfy", ["MaskingCore"] = "verification/Masking.dfy" };
    // The 1-based line of the opening brace of the body of a method, function or predicate.
    public static int BodyLine(string source, string symbol)
    {
        var masked = Requirements.Masked(source);
        var member = Requirements.DafnyMember(masked, symbol, @"(?:(?:static|ghost|opaque)\s+)*(?:method|function|predicate)");
        Assert.True(member is { Opening: >= 0 }, $"no declaration body: {symbol}");
        return masked[..member!.Value.Opening].Count(c => c == '\n') + 1;
    }
    public static string Lines(string text) => Regex.Replace(text, @"\{(\w+\.\w+\.\w+)([+-]\d+)\}", m =>
    {
        var symbol = m.Groups[1].Value;
        var source = Data.Text(Path.Combine(RequirementsTests.Root, Files[symbol.Split('.')[0]]));
        return (BodyLine(source, symbol) + int.Parse(m.Groups[2].Value)).ToString();
    });
    // A copy of a fixture record with every anchor in its strings replaced by the line number.
    public static JsonNode Rendered(JsonNode node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Rendered(p.Value)))),
        JsonArray a => new JsonArray(a.Select(v => v is null ? null : Rendered(v)).ToArray()),
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Lines(s))!,
        _ => node.DeepClone()
    };
}
