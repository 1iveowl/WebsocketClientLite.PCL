using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pdd;
using Xunit;

namespace Pdd.Tests;

public class RequirementsTests
{
    public static readonly string Root = Program.FindRoot();
    static JsonNode Manifest() => Data.Read(Path.Combine(Root, "verification/requirements.json"));
    public static IEnumerable<object[]> InvalidManifests() => Data.Read(Path.Combine(Root, "verification/tests/invalid-requirements.json")).AsArray().Select(f => new object[] { f!.ToJsonString() });
    internal static void Edit(JsonNode node, JsonArray keys, JsonNode? value, bool remove = false)
    {
        foreach (var key in keys.Take(keys.Count - 1))
            node = node is JsonArray a ? a[key!.GetValue<int>()]! : node[key!.ToString()]!;
        var last = keys[^1]!;
        if (node is JsonArray array)
        {
            if (remove)
                array.RemoveAt(last.GetValue<int>());
            else
                array[last.GetValue<int>()] = value?.DeepClone();
        }
        else
        {
            if (remove)
                node.AsObject().Remove(last.ToString());
            else
                node[last.ToString()] = value?.DeepClone();
        }
    }
    [Theory, MemberData(nameof(InvalidManifests))]
    public void MalformedManifest(string json)
    {
        var fixture = Data.Parse(json);
        var manifest = Manifest();
        Edit(manifest, fixture["path"]!.AsArray(), fixture["value"], fixture["remove"]?.GetValue<bool>() == true);
        var exception = Assert.ThrowsAny<Exception>(() => new Requirements(Root).Validate(manifest));
        Assert.Contains(fixture["error"]!.ToString(), exception.Message);
    }
    [Fact]
    public void ValidManifestAndTestedOnlyRequirements()
    {
        var manifest = new Requirements(Root).Validate();
        Assert.Equal(16, manifest.Requirements.Length);
        Assert.Empty(manifest.Requirements.Single(r => r.Id == "REASSEMBLY-003").Contracts);
        Assert.Empty(manifest.Requirements[3].Contracts);
        Assert.Empty(manifest.Requirements[4].Contracts);
    }
    [Fact]
    public void TestReferencesAcceptArrayArgumentsInAttributes()
    {
        using var tree = new TemporaryTree();
        File.WriteAllText(Path.Combine(tree.Path, "Demo.cs"), """
            namespace Demo.Tests;
            public class Rows
            {
                [Theory]
                [InlineData(new byte[] { 0x81, 126, 0, 125 })] // a comment ] [ after the row
                [InlineData(new byte[] { 0xC1, 0 })]
                public void Accepted(byte[] bytes) { }

                [InlineData(new byte[] { 0x81, 0 })]
                public void NotATest(byte[] bytes) { }
            }
            """);
        Assert.Contains("Accepted", Requirements.Resolve(tree.Path, new SourceReference("Demo.cs", "Demo.Tests.Rows.Accepted"), test: true));
        Assert.Contains("not an xUnit test", Assert.Throws<InvalidDataException>(() => Requirements.Resolve(tree.Path, new SourceReference("Demo.cs", "Demo.Tests.Rows.NotATest"), test: true)).Message);
    }
    [Fact]
    public void SchemaAndDuplicateKeysFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => Requirements.Shape(JsonValue.Create("anything"), Data.Parse("{\"format\":\"uri\"}")));
        Assert.Throws<InvalidDataException>(() => Data.Parse("{\"a\":1,\"a\":1}"));
        Assert.ThrowsAny<Exception>(() => Data.Parse("{"));
    }
    [Theory]
    [InlineData("review", "incomplete evidence")]
    [InlineData("proof", "contract without proof")]
    public void RequiredKinds(string kind, string message)
    {
        var manifest = Manifest();
        var obligations = manifest["requirements"]![0]!["obligations"]!.AsArray();
        foreach (var item in obligations.Where(o => o!["kind"]!.ToString() == kind).ToArray())
            obligations.Remove(item);
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => new Requirements(Root).Validate(manifest)).Message);
    }
    public static IEnumerable<object[]> Boundaries()
    {
        yield return ["[\"schema_version\"]", "99", "unsupported value/version"];
        yield return ["[\"boundaries\",1,\"id\"]", "\"CLOSE-LOCK\"", "duplicate boundary ID"];
        yield return ["[\"boundaries\",0,\"review_anchor\"]", "\"absent\"", "unresolved boundary anchor"];
        yield return ["[\"boundaries\",0,\"assumption\"]", "\" \"", "invalid string"];
        yield return ["[\"boundaries\",0,\"owners\"]", "[]", "too few entries"];
        yield return ["[\"boundaries\",0,\"owners\",0,\"file\"]", "\"../escape.cs\"", "path outside repository"];
        yield return ["[\"boundaries\",0,\"owners\",0,\"symbol\"]", "\"Demo.Tests.Absent\"", "unresolved or ambiguous"];
        yield return ["[\"boundaries\",0,\"requirements\"]", "[\"CLOSE-005\"]", "dependent requirements"];
        yield return ["[\"boundaries\",0,\"test_obligations\"]", "[\"CLOSE-001-PROOF\"]", "test obligations"];
        yield return ["[\"boundaries\",0,\"exclusions\"]", "[]", "too few entries"];
        yield return ["[\"boundaries\",0,\"approved\"]", "true", "unknown fields"];
    }
    [Theory, MemberData(nameof(Boundaries))]
    public void InvalidBoundary(string keys, string value, string message)
    {
        var register = Data.Read(Path.Combine(Root, "verification/boundaries.json"));
        Edit(register, Data.Parse(keys).AsArray(), Data.Parse(value));
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => new Requirements(Root).Validate(register: register)).Message);
    }
    [Fact]
    public void MissingBoundaryFieldsFilesAndDuplicateKeys()
    {
        var register = Data.Read(Path.Combine(Root, "verification/boundaries.json"));
        register["boundaries"]![0]!.AsObject().Remove("assumption");
        Assert.Contains("missing required", Assert.Throws<InvalidDataException>(() => new Requirements(Root).Validate(register: register)).Message);
        register = Data.Read(Path.Combine(Root, "verification/boundaries.json"));
        register["boundaries"]!.AsArray().RemoveAt(0);
        Assert.Contains("dangling boundary", Assert.Throws<InvalidDataException>(() => new Requirements(Root).Validate(register: register)).Message);
        using var tree = new TemporaryTree();
        Assert.Contains("missing file", Assert.Throws<InvalidDataException>(() => new Requirements(Root).Validate(sourceRoot: tree.Path)).Message);
        Directory.CreateDirectory(Path.Combine(tree.Path, "verification"));
        File.WriteAllText(Path.Combine(tree.Path, "verification/boundaries.json"), "{\"schema_version\":1,\"schema_version\":1}");
        Assert.Contains("duplicate JSON key", Assert.Throws<InvalidDataException>(() => new Requirements(Root).Validate(sourceRoot: tree.Path)).Message);
    }
    // A copy of the files Validate reads (verification/ and the C# sources), so that one
    // Dafny comment can be changed without touching the repository.
    static TemporaryTree SourceCopy()
    {
        var tree = new TemporaryTree();
        var files = Directory.EnumerateFiles(Path.Combine(Root, "verification"))
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories));
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(Root, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")) continue;
            var target = Path.Combine(tree.Path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return tree;
    }
    // The comment block directly above the line that contains `declaration`, replaced by
    // `comment` (one line), or removed when `comment` is null.
    static void ReplaceComment(string path, string declaration, string? comment)
    {
        var lines = File.ReadAllText(path).Split('\n').ToList();
        var index = lines.FindIndex(l => l.Contains(declaration, StringComparison.Ordinal));
        Assert.True(index > 0, $"declaration not found: {declaration}");
        var start = index;
        while (start > 0 && lines[start - 1].TrimStart().StartsWith("//", StringComparison.Ordinal)) start--;
        Assert.True(start < index, $"no comment above: {declaration}");
        lines.RemoveRange(start, index - start);
        if (comment is not null) lines.Insert(start, comment);
        File.WriteAllText(path, string.Join('\n', lines));
    }
    // Rule 1 of "Rules for verification work" (verification/README.md): every declaration in
    // the Dafny core names an RFC 6455 section or "Library policy:" in the comment block
    // directly above it. Each row removes or replaces one such block in a copy of the sources.
    public static IEnumerable<object?[]> UncitedDeclarations()
    {
        // No comment: the block above HeaderValid removed.
        yield return ["verification/Protocol.dfy", "static function HeaderValid(", null, "HeaderValid"];
        // A comment that names neither.
        yield return ["verification/Masking.dfy", "static method Apply(", "    // Masks the data with the key.", "Apply"];
        // "section" with no number, and "library policy" without the label's form.
        yield return ["verification/Protocol.dfy", "HeaderRoundTrip(", "    // Follows the library policy; see the section above.", "HeaderRoundTrip"];
        // The external contract: the block above its class removed.
        yield return ["verification/Protocol.dfy", "class NativeCopy {", null, "CopySlice"];
    }
    [Theory, MemberData(nameof(UncitedDeclarations))]
    public void UncitedDafnyDeclarationFails(string file, string declaration, string? comment, string name)
    {
        using var tree = SourceCopy();
        ReplaceComment(Path.Combine(tree.Path, file), declaration, comment);
        var message = Assert.Throws<InvalidDataException>(() => new Requirements(Root).Validate(sourceRoot: tree.Path)).Message;
        Assert.Contains(file + ":", message);
        Assert.Contains(name, message);
    }
    [Fact]
    public void EveryDafnyDeclarationIsCited()
    {
        var files = Citations.DafnyFiles(Root);
        Assert.Equal(["verification/Masking.dfy", "verification/Protocol.dfy"], files);
        foreach (var file in files)
        {
            var text = Data.Text(Path.Combine(Root, file));
            // Every line with a declaration keyword, counted apart from the declaration pattern.
            // Declaration keywords, counted apart from the declaration pattern; the method of a
            // function by method is part of its function.
            var masked = Citations.Masked(text);
            var keywords = Regex.Matches(masked, @"\b(?:method|function|predicate|lemma|constructor|iterator)\b").Count - Regex.Matches(masked, @"\bby\s+method\b").Count;
            Assert.True(keywords > 0, file);
            Assert.Equal(keywords, Citations.Dafny(file, text));
        }
    }
    [Fact]
    public void CitationFormsAccepted()
    {
        const string text = """
            module Demo {
              // RFC 6455 section 5.3.
              class A {
                static method First() {}
                // RFC 6455. Section 5.2: the length.
                static function Second(): bool { true }
                // RFC 6455 sections 5.2 and 5.5, mixed with library policy.
                static predicate Third() { true }
                // Library policy: a choice the RFC leaves open.
                ghost function Fourth(): int { 0 }
                // RFC 6455, continued below.
                // Section 5.7, the example.
                static method {:isolate_assertions} {:timeLimit 9} Fifth() {}
                // The words static lemma Hidden() in a comment declare nothing.
                // Library policy: the next one.
                static lemma Sixth() {}
                // RFC 6455 section 5.3, with an attribute line below.
                @IsolateAssertions
                static method Seventh(x': int) {}
                // Library policy: a constructor.
                constructor (x: int) requires x > 0 {}
              }
              // Library policy: an external class and its one member.
              class {:extern} B extends T {
                static method {:extern} Copy()
              }
              class F {
                /* outer /* inner */ static method Old() {} the lemma here */
                // RFC 6455 section 5.3: a function by method.
                function Tenth(): int { 0 }
                by method
                { return 0; }
              }
              // Library policy: a class line with a trailing comment.
              class D { // a note
                static method Only() {}
              }
              // RFC 6455 section 5.3.
              @Something
              class E {
                static method First() {}
                // RFC 6455 section 5.3: a function by method on one line.
                function Once(): int { 0 } by method { return 0; }
              }
            }
            """;
        Assert.Equal(13, Citations.Dafny("demo.dfy", text));
    }
    // Forms the first version of the check passed or skipped without an error (#169 review).
    public static IEnumerable<object[]> UncitedForms()
    {
        const string cited = "    // RFC 6455 section 5.3.\n    static method M() {}\n";
        string Module(string members) => "module D {\n  class C {\n" + cited + members + "  }\n}\n";
        yield return [Module("    constructor (x: int) requires x > 0 {}\n"), "demo.dfy:5: constructor"];
        yield return [Module("    least predicate P() { true }\n"), "demo.dfy:5: P"];
        yield return [Module("    greatest lemma L() ensures true {}\n"), "demo.dfy:5: L"];
        yield return [Module("    iterator I() yields (x: int) ensures true {}\n"), "demo.dfy:5: I"];
        yield return [Module("    class E { static method F() {} }\n"), "demo.dfy:5: unrecognised declaration form"];
        // A primed identifier must not hide the declarations after it.
        yield return [Module("    // RFC 6455 section 5.3.\n    function F2(x': int): int { 0 }\n    function G(): int { 0 }\n    // RFC 6455 section 5.3.\n    function H(t': int): int { 0 }\n"), "demo.dfy:7: G"];
        yield return [Module("    // RFC 7692 section 7.\n    static method Other() {}\n"), "demo.dfy:6: Other"];
        yield return [Module("    // See the Dafny reference manual, section 3.\n    static method Manual() {}\n"), "demo.dfy:6: Manual"];
        yield return [Module("    // Library policy:\n    static method Empty() {}\n"), "demo.dfy:6: Empty"];
        // Strings and comments that the first mask misread (#169 re-review).
        yield return [Module("    // RFC 6455 section 5.3.\n    const S := @\"line\n/* not a comment\";\n    function G(): int { 0 }\n    /* real */\n    // RFC 6455 section 5.3.\n    function H(): int { 0 }\n"), "demo.dfy:8: G"];
        yield return [Module("    const P := @\"C:\\\"; const Q := \"/*\";\n    function G(): int { 0 }\n    /* real */\n"), "demo.dfy:6: G"];
        yield return [Module("    /*\n    // RFC 6455 section 5.3.\n    */ method A() {}\n"), "demo.dfy:7: A"];
        // A second declaration on one line, a character outside the BMP, a backslash before a
        // newline, and a block comment before a line comment (#169 scanner review).
        yield return [Module("    // RFC 6455 section 5.3.\n    method A() {} method B() {}\n"), "demo.dfy:6: unrecognised declaration form"];
        yield return [Module("    // RFC 6455 section 5.3.\n    function F(): int { 0 }\n    } by method { return 0; } method Hidden() {}\n"), "demo.dfy:7: unrecognised declaration form"];
        yield return [Module("    const E := ['\U0001F600','\"']; method Hidden() {}\n"), "demo.dfy:5: unrecognised declaration form"];
        yield return [Module("    const S := \"a\\\n    method Hidden() {}\";\n"), "demo.dfy:6: Hidden"];
        yield return [Module("    /* RFC 6455 section 5.3. */ // see\n    method A() {}\n"), "demo.dfy:6: A"];
        // Primes beside character literals and a quote in a string hide nothing.
        yield return [Module("    // RFC 6455 section 5.3.\n    function F(x': char): bool { x' == '/' || x' == '\\'' || \"it's\" == \"\" }\n    function G(): int { 0 }\n"), "demo.dfy:7: G"];
    }
    [Theory, MemberData(nameof(UncitedForms))]
    public void UncitedFormFails(string text, string expected) =>
        Assert.Contains(expected, Assert.Throws<InvalidDataException>(() => Citations.Dafny("demo.dfy", text)).Message);
    [Fact]
    public void ClassCommentCitesOnlyTheFirstMember()
    {
        const string text = """
            module Demo {
              // Library policy: one external copy.
              class B {
                static method One() {}
                static method Two() {}
              }
            }
            """;
        var message = Assert.Throws<InvalidDataException>(() => Citations.Dafny("demo.dfy", text)).Message;
        Assert.Contains("demo.dfy:5: Two", message);
        Assert.DoesNotContain("One", message);
    }
    [Fact]
    public void ResolverRejectsCommentsCallsDuplicatesWrongScopeAndStrings()
    {
        string[] samples = ["namespace Demo;\nclass Tests {\n // [Fact]\n // public void Check() {}\n }", "namespace Demo;\nclass Tests {\n public void Other() { Check(); }\n }", "namespace Demo;\nclass Tests {\n [Fact]\n public void Check() {}\n [Fact]\n public void Check() {}\n }", "namespace Demo;\nclass Tests {}\nclass Other {\n [Fact]\n public void Check() {}\n }", "namespace Demo;\nclass Tests {\n class Nested {\n [Fact]\n public void Check() {}\n }\n }", "namespace Demo;\nclass Tests {\n string s = @\"\n public void Check() {}\n\";\n }"];
        using var tree = new TemporaryTree();
        var reference = new SourceReference("test.cs", "Demo.Tests.Check");
        foreach (var sample in samples)
        {
            File.WriteAllText(Path.Combine(tree.Path, "test.cs"), sample);
            Assert.Throws<InvalidDataException>(() => Requirements.Resolve(tree.Path, reference, true));
        }
        File.WriteAllText(Path.Combine(tree.Path, "test.cs"), "namespace Demo;\nclass Tests {\n [Fact]\n public void Check() {}\n}");
        Assert.Contains("Check", Requirements.Resolve(tree.Path, reference, true));
        File.WriteAllText(Path.Combine(tree.Path, "test.dfy"), "module Demo { class Core {\n static method Check()\n ensures true\n {}\n static method Other()\n ensures false\n {}\n}}");
        var header = Requirements.Resolve(tree.Path, new("test.dfy", "Demo.Core.Check"));
        Assert.Contains("ensures true", header);
        Assert.DoesNotContain("ensures false", header);
    }
    [Fact]
    public void SymlinkOutsideRootRejected()
    {
        using var tree = new TemporaryTree();
        using var other = new TemporaryTree();
        File.WriteAllText(Path.Combine(other.Path, "file"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(tree.Path, "link"), other.Path);
        Assert.Throws<InvalidDataException>(() => Requirements.Source(tree.Path, "link/file"));
    }
    [Fact]
    public void Utf8AndBomMatchPythonTextBehavior()
    {
        using var tree = new TemporaryTree();
        var path = Path.Combine(tree.Path, "text.cs");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, 0x41, 0x0D, 0x0A]);
        Program.Normalize(path, "/tool");
        Assert.Equal("// <auto-generated/>\n#nullable disable\n\uFEFFA\n", Data.Text(path));
        File.WriteAllBytes(path, [0xFF]);
        Assert.Throws<System.Text.DecoderFallbackException>(() => Data.Text(path));
    }
    [Fact]
    public void GeneratedNormalizationMatchesLegacyBytes()
    {
        using var tree = new TemporaryTree();
        var path = Path.Combine(tree.Path, "input.cs");
        File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("// /tmp/tool\r\npublic partial class A {}\rpublic partial class B {}\n"));
        Program.Normalize(path, "/tmp/tool");
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("// <auto-generated/>\n#nullable disable\n// <pinned-toolchain>\ninternal partial class A {}\ninternal partial class B {}\n"), File.ReadAllBytes(path));
    }
}
