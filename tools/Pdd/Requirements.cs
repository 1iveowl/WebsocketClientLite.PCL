using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Pdd.Data;

namespace Pdd;

public sealed class Requirements(string root)
{
    public static void Shape(JsonNode? value, JsonNode schema, string where = "$")
    {
        string[] supported = ["$schema", "title", "type", "properties", "required", "additionalProperties", "items", "minItems", "uniqueItems", "minLength", "pattern", "enum", "const"];
        Require(schema.AsObject().All(p => supported.Contains(p.Key)), $"{where}: unsupported schema keyword");
        if (schema.AsObject().ContainsKey("const"))
            Require(JsonNode.DeepEquals(value, schema["const"]), $"{where}: unsupported value/version");
        if (schema["enum"] is JsonArray choices)
            Require(choices.Any(c => JsonNode.DeepEquals(value, c)), $"{where}: invalid choice");
        if (schema["type"] is { } type)
        {
            var valid = type.ToString() switch
            {
                "object" => value is JsonObject,
                "array" => value is JsonArray,
                "string" => value is JsonValue v && v.TryGetValue<string>(out _),
                _ => throw new InvalidDataException($"{where}: unsupported schema type")
            };
            Require(valid, $"{where}: expected {type}");
        }
        if (value is JsonObject obj)
        {
            var props = schema["properties"]?.AsObject() ?? [];
            Require((schema["required"]?.AsArray() ?? []).All(k => obj.ContainsKey(k!.ToString())), $"{where}: missing required fields");
            if (schema["additionalProperties"]?.ToString() == "false")
                Require(obj.All(p => props.ContainsKey(p.Key)), $"{where}: unknown fields");
            foreach (var (key, item) in obj)
                if (props[key] is { } sub)
                    Shape(item, sub, $"{where}.{key}");
        }
        if (value is JsonArray array)
        {
            Require(array.Count >= (schema["minItems"]?.GetValue<int>() ?? 0), $"{where}: too few entries");
            if (schema["uniqueItems"]?.GetValue<bool>() == true)
                for (int i = 0; i < array.Count; i++)
                    Require(!array.Take(i).Any(x => JsonNode.DeepEquals(x, array[i])), $"{where}: duplicate entries");
            for (int i = 0; i < array.Count; i++)
                Shape(array[i], schema["items"]!, $"{where}[{i}]");
        }
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            Require(text.Length >= (schema["minLength"]?.GetValue<int>() ?? 0), $"{where}: empty string");
            if (schema["pattern"] is { } pattern)
                Require(Regex.IsMatch(text, pattern.ToString()), $"{where}: invalid string");
        }
    }
    public static string Source(string root, string name)
    {
        var basePath = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, name));
        Require(!Path.IsPathRooted(name) && path.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal), $"path outside repository: {name}");
        // Resolve every symlink component, including parent directories.
        var resolved = basePath;
        foreach (var part in Path.GetRelativePath(basePath, path).Split(Path.DirectorySeparatorChar))
        {
            resolved = Path.Combine(resolved, part);
            FileSystemInfo info = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
            if (info.LinkTarget is not null)
                resolved = info.ResolveLinkTarget(true)!.FullName;
            Require(resolved.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal), $"path outside repository: {name}");
        }
        Require(File.Exists(resolved), $"missing file: {name}");
        return Text(resolved);
    }
    public static string Masked(string text) => Regex.Replace(text, "//[^\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", m => Regex.Replace(m.Value, "[^\\n]", " "));
    public static string Body(string text, int opening)
    {
        Require(opening >= 0, "missing declaration body");
        int depth = 0;
        for (int i = opening; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return text[(opening + 1)..i];
        }
        throw new InvalidDataException("unbalanced declaration body");
    }
    static Match One(string text, string pattern, string label)
    {
        var matches = Regex.Matches(text, pattern, RegexOptions.Multiline);
        Require(matches.Count == 1, $"unresolved or ambiguous {label} ({matches.Count} declarations)");
        return matches[0];
    }
    // The one Dafny declaration lookup, shared by Resolve and the mutation classifier.
    // `symbol` is Module.Class.Member and `kinds` the keywords before the name, for example
    // @"static\s+method". The match is limited to that module and class, and allows any
    // number of attributes before the name. Returns the index of the declaration in `masked`
    // and of its body's opening brace, which is -1 when the declaration has no body, or null
    // when the class has no such member. An ambiguous or unbalanced source throws.
    public static (int Index, int Opening)? DafnyMember(string masked, string symbol, string kinds, string? label = null)
    {
        label ??= symbol;
        var parts = symbol.Split('.');
        Require(parts.Length >= 3, $"invalid qualified symbol: {label}");
        var module = One(masked, @"\bmodule\s+(?:\{:[^}]*\}\s*)*" + Regex.Escape(string.Join('.', parts[..^2])) + @"\s*\{", label);
        var moduleOpen = module.Index + module.Length - 1;
        var cls = One(Body(masked, moduleOpen), @"\bclass\s+" + Regex.Escape(parts[^2]) + @"\b", label);
        var classOpen = masked.IndexOf('{', moduleOpen + 1 + cls.Index + cls.Length);
        var scope = Body(masked, classOpen);
        var matches = Regex.Matches(scope, @"^\s*" + kinds + @"\s+(?:\{:[^}]*\}\s*)*" + Regex.Escape(parts[^1]) + @"\s*[(<]", RegexOptions.Multiline);
        if (matches.Count == 0) return null;
        Require(matches.Count == 1, $"unresolved or ambiguous {label} ({matches.Count} declarations)");
        var before = scope[..matches[0].Index];
        Require(before.Count(c => c == '{') == before.Count(c => c == '}'), $"nested declaration: {label}");
        var index = classOpen + 1 + matches[0].Index;
        return (index, BodyOpening(masked, index + matches[0].Length - 1));
    }
    static readonly Regex NextDeclaration = new(@"\G(?:static|ghost|opaque|twostate|function|predicate|method|lemma|constructor|class|trait|datatype|codatatype|newtype|type|const|module|import|iterator)\b");
    static readonly Regex OperandExpected = new(@"(?:[(\[,=!&+\-/%^~<:]|\b(?:in|requires|ensures|reads|modifies|decreases|then|else|multiset|iset))\s*$");
    // The opening brace of a declaration's body, scanning from its parameter list, or -1 when
    // the next declaration or the end of the class comes first. Attributes and braces inside
    // parentheses or brackets are skipped, and so is a set display in a contract: a brace after
    // an operator or a keyword that expects an operand, or, after `|`, `>` or `*`, a brace whose
    // closing brace is followed by more of the declaration instead of the next one.
    static int BodyOpening(string masked, int from)
    {
        bool EndsDeclaration(int at)
        {
            while (at < masked.Length && char.IsWhiteSpace(masked[at])) at++;
            return at == masked.Length || masked[at] == '}' || NextDeclaration.IsMatch(masked, at);
        }
        var depth = 0;
        for (var i = from; i < masked.Length; i++)
        {
            var c = masked[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (depth == 0 && c == '}') return -1;
            else if (depth == 0 && !char.IsLetterOrDigit(masked[i - 1]) && masked[i - 1] != '_' && NextDeclaration.IsMatch(masked, i)) return -1;
            else if (c == '{')
            {
                var close = i + 1 + Body(masked, i).Length;
                var next = i + 1;
                while (next < masked.Length && char.IsWhiteSpace(masked[next])) next++;
                var before = masked[from..i].TrimEnd();
                var display = depth > 0 || (next < masked.Length && masked[next] == ':') || OperandExpected.IsMatch(before)
                    || (before.Length > 0 && before[^1] is '|' or '>' or '*' && !EndsDeclaration(close + 1));
                if (!display) return i;
                i = close;
            }
        }
        return -1;
    }
    public static string Resolve(string root, SourceReference reference, bool test = false)
    {
        var label = $"{reference.File}:{reference.Symbol}";
        var text = Masked(Source(root, reference.File));
        var parts = reference.Symbol.Split('.');
        Require(parts.Length >= 3, $"invalid qualified symbol: {label}");
        var owner = parts[^2];
        var method = parts[^1];
        var ns = string.Join('.', parts[..^2]);
        if (reference.File.EndsWith(".dfy"))
        {
            var member = DafnyMember(text, reference.Symbol, @"static\s+method", label);
            Require(member is not null, $"unresolved or ambiguous {label} (0 declarations)");
            Require(!test, $"not an xUnit test declaration: {label}");
            Require(member!.Value.Opening >= 0, $"missing declaration body: {label}");
            return text[member.Value.Index..member.Value.Opening];
        }
        Require(reference.File.EndsWith(".cs"), $"unsupported source type: {label}");
        One(text, @"^namespace\s+" + Regex.Escape(ns) + @"\s*;", label);
        var scope = text;
        var prefix = @"^\s*(?:public|internal|private|protected)\s+(?:(?:static|async|virtual|override)\s+)*[\w<>?,\[\].]+\s+";
        var cls = One(scope, @"\bclass\s+" + Regex.Escape(owner) + @"\b", label);
        scope = Body(scope, scope.IndexOf('{', cls.Index + cls.Length));
        var declaration = One(scope, prefix + Regex.Escape(method) + @"\s*\(", label);
        var before = scope[..declaration.Index];
        Require(before.Count(c => c == '{') == before.Count(c => c == '}'), $"nested declaration: {label}");
        if (test)
            Require(Regex.IsMatch(before, @"\[(?:Fact|Theory)\](?:\s*\[(?:[^\[\]]|\[\])*\])*\s*$"), $"not an xUnit test declaration: {label}");
        var end = scope.IndexOf('{', declaration.Index + declaration.Length);
        if (reference.File.EndsWith(".cs"))
        {
            var arrow = scope.IndexOf("=>", declaration.Index + declaration.Length, StringComparison.Ordinal);
            if (arrow >= 0 && (end < 0 || arrow < end)) end = arrow;
        }
        Require(end >= 0, $"missing declaration body: {label}");
        return scope[declaration.Index..end];
    }
    // Fold only more-indented continuation lines in the repository's Dafny style.
    public static string LogicalLine(string[] lines, int index)
    {
        var first = lines[index];
        var indent = first.Length - first.TrimStart().Length;
        var text = first.Trim();
        for (int i = index + 1; i < lines.Length && lines[i].Trim().Length > 0 && lines[i].Length - lines[i].TrimStart().Length > indent; i++)
            text += " " + lines[i].Trim();
        return Regex.Replace(text, @"\s+", " ");
    }
    public static string[] Clauses(string source, string kinds = "requires|ensures|modifies")
    {
        var lines = Masked(source).Split('\n');
        return Enumerable.Range(0, lines.Length).Where(i => Regex.IsMatch(lines[i].TrimStart(), "^(" + kinds + @")\b"))
            .Select(i => LogicalLine(lines, i)).ToArray();
    }
    public RequirementManifest Validate(JsonNode? manifest = null, JsonNode? register = null, string? sourceRoot = null)
    {
        sourceRoot ??= root;
        manifest ??= Read(Path.Combine(root, "verification/requirements.json"));
        Shape(manifest, Read(Path.Combine(root, "verification/requirements.schema.json")));
        var model = Model<RequirementManifest>(manifest);
        register ??= Parse(Source(sourceRoot, model.BoundariesFile));
        Shape(register, Read(Path.Combine(root, "verification/boundaries.schema.json")));
        var boundaries = Model<BoundaryRegister>(register).Boundaries;
        var boundaryIds = new HashSet<string>();
        var review = Source(sourceRoot, "verification/close-intent-review.md");
        foreach (var entry in boundaries)
        {
            Require(boundaryIds.Add(entry.Id), $"duplicate boundary ID: {entry.Id}");
            Require(Regex.Matches(review, Regex.Escape($"<a id=\"{entry.ReviewAnchor}\"></a>")).Count == 1, $"unresolved boundary anchor: {entry.ReviewAnchor}");
        }
        var ids = new HashSet<string>();
        var obligations = new HashSet<string>();
        foreach (var req in model.Requirements)
        {
            Require(ids.Add(req.Id), $"duplicate requirement ID: {req.Id}");
            Require(review.Contains($"### {req.Id}\n"), $"missing intent review: {req.Id}");
            Require(req.Assumptions.All(boundaryIds.Contains), $"{req.Id}: dangling boundary reference");
            foreach (var c in req.Contracts)
            {
                var lines = Clauses(Resolve(sourceRoot, c));
                foreach (var clause in c.Clauses)
                    Require((clause.StartsWith("requires ") || clause.StartsWith("ensures ") || clause.StartsWith("modifies ")) && lines.Contains(clause), $"{req.Id}: unresolved contract clause: {clause}");
            }
            foreach (var p in req.ProductionPaths)
                Resolve(sourceRoot, p.Reference);
            var kinds = new HashSet<string>();
            foreach (var o in req.Obligations)
            {
                Require(obligations.Add(o.Id), $"duplicate obligation ID: {o.Id}");
                kinds.Add(o.Kind);
                if (o.Delivery == "existing" && o.Kind is "proof" or "test")
                    Require(o.References.Length > 0, $"{o.Id}: existing evidence requires references");
                if (o.Kind == "proof")
                {
                    Require(req.Contracts.Length > 0, $"{o.Id}: proof requires a contract");
                    Require(o.References.Length > 0 && o.References.All(r => req.Contracts.Any(c => r.File == c.File && r.Symbol == c.Symbol)), $"{o.Id}: proof reference must match a contract");
                }
                foreach (var r in o.References)
                    Resolve(sourceRoot, r, o.Kind == "test");
            }
            Require(kinds.Contains("review") && (kinds.Contains("proof") || kinds.Contains("test")), $"{req.Id}: incomplete evidence obligations");
            Require(req.Contracts.Length == 0 || kinds.Contains("proof"), $"{req.Id}: contract without proof obligation");
        }
        foreach (var b in boundaries)
        {
            var dependents = model.Requirements.Where(r => r.Assumptions.Contains(b.Id)).ToArray();
            Require(b.Requirements.ToHashSet().SetEquals(dependents.Select(r => r.Id)), $"{b.Id}: dependent requirements must match assumption references");
            Require(b.TestObligations.ToHashSet().SetEquals(dependents.SelectMany(r => r.Obligations).Where(o => o.Kind == "test").Select(o => o.Id)), $"{b.Id}: test obligations must match dependent requirements");
            foreach (var owner in b.Owners)
                Resolve(sourceRoot, owner);
        }
        return model;
    }
}
