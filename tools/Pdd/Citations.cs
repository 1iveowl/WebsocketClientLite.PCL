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
    static readonly Regex PolicyText = new(@"\bLibrary policy:\s*\w");
    static readonly Regex RfcSection = new(@"^https://www\.rfc-editor\.org/rfc/rfc6455\.html#section-\d+(?:\.\d+)*$");
    static readonly Regex OriginPolicy = new(@"^Library policy:\s*\w");
    const string Keywords = "method|function|predicate|lemma|constructor|iterator";
    // A declaration that starts its line, with any modifiers and attributes before its name.
    // A constructor may have no name.
    static readonly Regex Declaration = new(@"^[ \t]*(?:(?:static|ghost|opaque|twostate|least|greatest)\s+)*(" + Keywords + @")\b(?:\s*\{:[^}]*\})*\s*(\w*)", RegexOptions.Multiline);
    static readonly Regex Keyword = new(@"\b(?:" + Keywords + @")\b");
    // The method part of a function by method, on its own line: part of the function above.
    static readonly Regex ByMethod = new(@"^\s*(?:\}\s*)?by\s+method\b");
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

    // `text` with Dafny comments and strings blanked and its lines kept, and the 0-based lines
    // on which a `//` comment starts outside any other comment or string. Unlike
    // Requirements.Masked, which reads C#, this reads Dafny: block comments nest, a verbatim
    // string (@"...") may span lines and doubles its quotes, a prime after a name is part of it
    // (x'), and a character literal is one character or one escape on one line.
    public static (string Masked, HashSet<int> LineComments) Scan(string text)
    {
        var c = text.ToCharArray();
        var lineComments = new HashSet<int>();
        var line = 0;
        void Blank(int from, int to)
        {
            for (var k = from; k < to; k++)
                if (c[k] == '\n') line++; else c[k] = ' ';
        }
        bool At(int k, char a, char b) => k + 1 < text.Length && text[k] == a && text[k + 1] == b;
        var i = 0;
        while (i < text.Length)
        {
            int end;
            if (At(i, '/', '/'))
            {
                lineComments.Add(line);
                end = text.IndexOf('\n', i);
                if (end < 0) end = text.Length;
            }
            else if (At(i, '/', '*'))
            {
                var depth = 0;
                end = i;
                while (end < text.Length)
                {
                    if (At(end, '/', '*')) { depth++; end += 2; }
                    else if (At(end, '*', '/')) { end += 2; if (--depth == 0) break; }
                    else end++;
                }
            }
            else if (At(i, '@', '"'))
            {
                end = i + 2;
                while (end < text.Length && !(text[end] == '"' && !At(end, '"', '"'))) end += At(end, '"', '"') ? 2 : 1;
                end = Math.Min(end + 1, text.Length);
            }
            else if (text[i] == '"')
            {
                end = i + 1;
                while (end < text.Length && text[end] is not '"' and not '\n') end += text[end] == '\\' ? 2 : 1;
                end = Math.Min(end + 1, text.Length);
            }
            else if (text[i] == '\'' && (i == 0 || !(char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '\'' or '?')))
            {
                var escape = i + 1 < text.Length && text[i + 1] == '\\';
                end = i + 3 <= text.Length ? text.IndexOf('\'', escape ? Math.Min(i + 3, text.Length - 1) : i + 1) : -1;
                var close = text.IndexOf('\n', i);
                if (end < 0 || (close >= 0 && end > close) || (!escape && end != i + 2)) { i++; continue; }
                end++;
            }
            else
            {
                if (text[i] == '\n') line++;
                i++;
                continue;
            }
            Blank(i, end);
            i = end;
        }
        return (new string(c), lineComments);
    }
    public static string Masked(string text) => Scan(text).Masked;

    // Checks every declaration in `text`, the contents of `file`, and returns how many there
    // are. Every uncited declaration, and every line with a declaration keyword that is not a
    // declaration this check reads, is named in one error. The citation is the block of `//`
    // comment lines directly above the declaration, past any `@` attribute lines. For the
    // first member of a class with no comment of its own, the block above the class line, past
    // its `@` lines, is read instead.
    public static int Dafny(string file, string text)
    {
        var (masked, lineComments) = Scan(text);
        var lines = text.Split('\n');
        var maskedLines = masked.Split('\n');
        bool Comment(int k) => lineComments.Contains(k) && maskedLines[k].Trim().Length == 0;
        int PastAttributes(int k)
        {
            while (k >= 0 && maskedLines[k].TrimStart().StartsWith('@')) k--;
            return k;
        }
        var declared = new HashSet<int>();
        var uncited = new List<string>();
        foreach (Match m in Declaration.Matches(masked))
        {
            var line = Requirements.LineAt(masked, m.Index);
            declared.Add(line);
            var above = PastAttributes(line - 2);
            var block = new List<string>();
            while (true)
            {
                while (above >= 0 && Comment(above))
                    block.Insert(0, lines[above--].TrimStart().TrimStart('/').Trim());
                if (block.Count > 0 || above < 0 || !ClassOpening.IsMatch(maskedLines[above])) break;
                above = PastAttributes(above - 1);
            }
            var comment = string.Join(' ', block);
            if (!(Rfc.IsMatch(comment) && Section.IsMatch(comment)) && !PolicyText.IsMatch(comment))
                uncited.Add($"{file}:{line}: {(m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : m.Groups[1].Value)}");
        }
        for (var i = 0; i < maskedLines.Length; i++)
            if (!declared.Contains(i + 1) && Keyword.IsMatch(maskedLines[i]) && !ByMethod.IsMatch(maskedLines[i]))
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
