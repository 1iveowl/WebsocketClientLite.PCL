using System.Text.RegularExpressions;
using static Pdd.Data;

namespace Pdd;

// Rule 1 of "Rules for verification work" (verification/README.md) as a check: the Dafny core
// and the requirement map name where each rule comes from, an RFC 6455 section or the
// library's own policy. The check sees that a citation is there, not that it is the right one.
public static class Citations
{
    public static readonly string[] DafnyFiles = ["verification/Protocol.dfy", "verification/Masking.dfy"];
    public const string Policy = "Library policy:";
    // "RFC 6455 section 5.3", "Section 5.2:", "sections 5.2 and 5.5": a section number.
    static readonly Regex Section = new(@"\b[Ss]ections?\s+\d+(?:\.\d+)*");
    static readonly Regex RfcSection = new(@"^https://www\.rfc-editor\.org/rfc/rfc6455\.html#section-\d+(?:\.\d+)*$");
    // A method, function, predicate or lemma, with any modifiers and attributes before its name.
    static readonly Regex Declaration = new(@"^[ \t]*(?:(?:static|ghost|opaque|twostate)\s+)*(?:method|function|predicate|lemma)\b(?:\s*\{:[^}]*\})*\s+(\w+)", RegexOptions.Multiline);
    static readonly Regex ClassOpening = new(@"^\s*class\s+\w+\s*\{\s*$");

    // Checks every declaration in `text`, the contents of `file`, and returns how many there
    // are. Every uncited declaration is named in one error. The citation is the comment block directly above the declaration; for the first
    // member of a class, the block directly above the class counts too.
    public static int Dafny(string file, string text)
    {
        var masked = Requirements.Masked(text);
        var lines = text.Split('\n');
        var count = 0;
        var uncited = new List<string>();
        foreach (Match m in Declaration.Matches(masked))
        {
            var line = Requirements.LineAt(masked, m.Index);
            var block = new List<string>();
            var above = line - 2;
            while (true)
            {
                while (above >= 0 && lines[above].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    block.Add(lines[above--]);
                if (block.Count > 0 || above < 0 || !ClassOpening.IsMatch(lines[above])) break;
                above--;
            }
            var comment = string.Join(' ', block);
            if (!Section.IsMatch(comment) && !comment.Contains(Policy, StringComparison.Ordinal))
                uncited.Add($"{file}:{line}: {m.Groups[1].Value}");
            count++;
        }
        Require(uncited.Count == 0, $"no RFC 6455 section or \"{Policy}\" in the comment directly above: {string.Join(", ", uncited)}");
        return count;
    }

    // A requirement's origin holds an RFC 6455 section link or an entry that starts with the
    // policy label.
    public static void Origin(string id, string[] origin) =>
        Require(origin.Any(o => RfcSection.IsMatch(o) || o.StartsWith(Policy, StringComparison.Ordinal)),
            $"{id}: origin has no RFC 6455 section link or \"{Policy}\" entry");
}
