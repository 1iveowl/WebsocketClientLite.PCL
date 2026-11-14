using System.Text.RegularExpressions;
using static Pdd.Data;

namespace Pdd;

// Rule 1 of "Rules for verification work" (verification/README.md) as a check: the Dafny core
// and the requirement map name where each rule comes from, an RFC 6455 section or the
// library's own policy. The check sees that a citation is there, not that it is the right one.
public static class Citations
{
    public const string Policy = "Library policy:";
    static readonly Regex Rfc = new(@"\bRFC 6455\b");
    // "section 5.3", "Section 5.2:", "sections 5.2 and 5.5": a section number. With "RFC 6455"
    // somewhere in the same block, so that a continuation line may say "Section 5.2:".
    static readonly Regex Section = new(@"\b[Ss]ections?\s+\d+(?:\.\d+)*");
    // The label followed by the policy, not the label alone.
    static readonly Regex PolicyText = new(@"\bLibrary policy:\s*\S");
    static readonly Regex RfcSection = new(@"^https://www\.rfc-editor\.org/rfc/rfc6455\.html#section-\d+(?:\.\d+)*$");
    static readonly Regex OriginPolicy = new(@"^Library policy:\s*\S");
    const string Keywords = "method|function|predicate|lemma|constructor|iterator";
    // A declaration that starts its line, with any modifiers and attributes before its name.
    // A constructor may have no name.
    static readonly Regex Declaration = new(@"^[ \t]*(?:(?:static|ghost|opaque|twostate|least|greatest)\s+)*(" + Keywords + @")\b(?:\s*\{:[^}]*\})*\s*(\w*)", RegexOptions.Multiline);
    static readonly Regex Keyword = new(@"\b(?:" + Keywords + @")\b");
    static readonly Regex ClassOpening = new(@"^\s*class\b(?:\s*\{:[^}]*\})*[^{]*\{\s*$");

    // The Dafny sources the check reads: every .dfy file directly in verification/.
    public static string[] DafnyFiles(string root)
    {
        var directory = Path.Combine(root, "verification");
        var files = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.dfy").Select(f => "verification/" + Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray()
            : [];
        Require(files.Length > 0, "missing file: verification/*.dfy");
        return files;
    }

    // `text` with Dafny comments and strings blanked and its lines kept. Unlike
    // Requirements.Masked, which reads C#, a prime belongs to an identifier (x') and a
    // character literal is one character on one line.
    public static string Masked(string text) => Regex.Replace(text, @"//[^\n]*|/\*[\s\S]*?\*/|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'", m => Regex.Replace(m.Value, "[^\n]", " "));

    // Checks every declaration in `text`, the contents of `file`, and returns how many there
    // are. Every uncited declaration, and every line with a declaration keyword that is not a
    // declaration this check reads, is named in one error. The citation is the block of `//`
    // lines directly above the declaration, past any `@` attribute lines; for the first member
    // of a class, the block directly above the class line counts too.
    public static int Dafny(string file, string text)
    {
        var masked = Masked(text);
        var lines = text.Split('\n');
        var maskedLines = masked.Split('\n');
        var declared = new HashSet<int>();
        var uncited = new List<string>();
        foreach (Match m in Declaration.Matches(masked))
        {
            var line = Requirements.LineAt(masked, m.Index);
            declared.Add(line);
            var above = line - 2;
            while (above >= 0 && lines[above].TrimStart().StartsWith('@')) above--;
            var block = new List<string>();
            while (true)
            {
                while (above >= 0 && lines[above].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    block.Insert(0, lines[above--].TrimStart().TrimStart('/').Trim());
                if (block.Count > 0 || above < 0 || !ClassOpening.IsMatch(lines[above])) break;
                above--;
            }
            var comment = string.Join(' ', block);
            if (!(Rfc.IsMatch(comment) && Section.IsMatch(comment)) && !PolicyText.IsMatch(comment))
                uncited.Add($"{file}:{line}: {(m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : m.Groups[1].Value)}");
        }
        for (var i = 0; i < maskedLines.Length; i++)
            if (!declared.Contains(i + 1) && Keyword.IsMatch(maskedLines[i]))
                uncited.Add($"{file}:{i + 1}: unrecognised declaration form");
        Require(uncited.Count == 0, $"no RFC 6455 section or \"{Policy}\" with the policy in the comment directly above: {string.Join(", ", uncited)}");
        return declared.Count;
    }

    // A requirement's origin holds an RFC 6455 section link or an entry that starts with the
    // policy label and states the policy.
    public static void Origin(string id, string[] origin) =>
        Require(origin.Any(o => RfcSection.IsMatch(o) || OriginPolicy.IsMatch(o)),
            $"{id}: origin has no RFC 6455 section link or \"{Policy}\" entry");
}
