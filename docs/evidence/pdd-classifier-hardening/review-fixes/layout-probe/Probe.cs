// Prints the body-brace line of every method, function, predicate and lemma in the two Dafny
// sources, as Requirements.DafnyMember finds it (M6 fix of #145, 2026-11-12).
using System.Text.RegularExpressions;
var root = args[0];
const string Kinds = @"(?:(?:static|ghost|opaque|twostate)\s+)*(?:method|function|predicate|lemma)";
foreach (var file in new[] { "verification/Protocol.dfy", "verification/Masking.dfy" })
{
    var masked = Pdd.Requirements.Masked(File.ReadAllText(Path.Combine(root, file)));
    string module = "";
    foreach (Match m in Regex.Matches(masked, @"\bmodule\s+(?:\{:[^}]*\}\s*)*(\w+)|\bclass\s+(\w+)|^\s*" + Kinds + @"\s+(?:\{:[^}]*\}\s*)*(\w+)\s*[(<]", RegexOptions.Multiline))
    {
        if (m.Groups[1].Success) { module = m.Groups[1].Value; continue; }
        if (m.Groups[2].Success) { cls = m.Groups[2].Value; continue; }
        var symbol = $"{module}.{cls}.{m.Groups[3].Value}";
        string result;
        try
        {
            var member = Pdd.Requirements.DafnyMember(masked, symbol, Kinds);
            result = member is null ? "null" : $"declaration line {masked[..member.Value.Index].Count(c => c == '\n') + 1 + masked[member.Value.Index..].TakeWhile(char.IsWhiteSpace).Count(c => c == '\n')}, body brace line {(member.Value.Opening < 0 ? "none" : (masked[..member.Value.Opening].Count(c => c == '\n') + 1).ToString())}";
        }
        catch (InvalidDataException e) { result = "throws: " + e.Message; }
        Console.WriteLine($"{file} {symbol}: {result}");
    }
}
public partial class Program { static string cls = ""; }
