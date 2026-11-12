using Pdd;
using Xunit;

namespace Pdd.Tests;

// Findings 4 and 5 of the #136 code review (session plan step S10, 2026-11-11). A "this
// proposition could not be proved" location counts only inside the body of a function or
// predicate that the target clause names, declared in the mutant's module and class. Each
// case edits a copy of Protocol.dfy in memory and finds every line it uses from that copy.
public class ClassifierHardeningTests
{
    static string Root => RequirementsTests.Root;
    static string Protocol => Data.Text(Path.Combine(Root, "verification/Protocol.dfy"));
    static Mutant Header => new Mutations(Root).LoadCatalog(scope: "payload").Mutants.Single(m => m.Id == "HEADER-001-short-sixteen-bit");
    const string HeaderValidDeclaration = "    static function HeaderValid(first: bv8, second: bv8, length: bv64, limit: bv64): bool {";
    const string LengthConjunct = "       else if marker == 126 then 126 <= length <= 65535 else length >= 65536) &&";

    // The 1-based line of the only line equal to `text`, or of the first line after `after`
    // that equals it.
    static int Line(string source, string text, string? after = null)
    {
        var lines = source.Split('\n');
        var start = after is null ? 0 : Array.FindIndex(lines, l => l.Contains(after, StringComparison.Ordinal));
        Assert.True(start >= 0, $"line not found: {after}");
        var index = Array.FindIndex(lines, start, l => l == text);
        Assert.True(index >= 0, $"line not found: {text}");
        if (after is null)
            Assert.Equal(index, Array.FindLastIndex(lines, l => l == text));
        return index + 1;
    }
    // The verifier output of a postcondition failure in DecodeHeader with a "this proposition"
    // location on the given line, in the form Dafny 4.11 prints it.
    static ExecutionResult Failure(string source, string clause, int proposition) => new()
    {
        ExitCode = 4,
        Stdout = $"verification/Protocol.dfy({Line(source, "    {", "static method DecodeHeader(")},4): Error: a postcondition could not be proved on this return path\n" +
                 $"verification/Protocol.dfy({Line(source, "      " + clause)},41): Related location: this is the postcondition that could not be proved\n" +
                 $"verification/Protocol.dfy({proposition},38): Related location: this proposition could not be proved\n" +
                 "Dafny program verifier finished with 573 verified, 1 error\n",
        ElapsedSeconds = .1
    };
    static string Edit(string source, string before, string after)
    {
        Assert.Equal(2, source.Split(before).Length);
        return source.Replace(before, after, StringComparison.Ordinal);
    }

    [Fact]
    public void UneditedSourceIsDetected()
    {
        var mutant = Header;
        Assert.Equal("detected", Mutations.Classify(Failure(Protocol, mutant.TargetClause, Line(Protocol, LengthConjunct)), Protocol, mutant).Status);
    }

    // Finding 4. A function without a body does not take the next declaration's body.
    [Fact]
    public void BodylessNamedFunctionDoesNotTakeTheNextBody()
    {
        var mutant = Header;
        var clause = mutant.TargetClause.Replace("HeaderValid(", "Abstract(", StringComparison.Ordinal);
        var source = Edit(Protocol, HeaderValidDeclaration, "    static function Abstract(first: bv8, second: bv8, length: bv64, limit: bv64): bool\n" + HeaderValidDeclaration);
        source = Edit(source, "      " + mutant.TargetClause + "\n", "      " + clause + "\n");
        var record = Failure(source, clause, Line(source, LengthConjunct));
        Assert.Equal("inconclusive", Mutations.Classify(record, source, mutant with { TargetClause = clause, AllowedClauses = [clause] }).Status);
    }

    // Finding 4. A set display in a contract of a named function is not its body.
    [Theory]
    [InlineData("      requires limit in {0, limit}")]
    [InlineData("      ensures HeaderValid(first, second, length, limit) ==> length in {length}")]
    public void SetDisplayInAContractIsNotTheBody(string contract)
    {
        var mutant = Header;
        var source = Edit(Protocol, HeaderValidDeclaration, HeaderValidDeclaration[..^2] + "\n" + contract + "\n    {");
        Assert.Equal("detected", Mutations.Classify(Failure(source, mutant.TargetClause, Line(source, LengthConjunct)), source, mutant).Status);
    }

    // Finding 4. A body that cannot be read, or a named predicate declared twice, is
    // inconclusive instead of an exception that stops the suite.
    [Theory]
    [InlineData("unbalanced")]
    [InlineData("duplicate")]
    public void UnreadableNamedPredicateIsInconclusive(string mode)
    {
        var mutant = Header;
        var source = mode == "unbalanced"
            ? Edit(Protocol, LengthConjunct, LengthConjunct + " {{{")
            : Edit(Protocol, HeaderValidDeclaration, "    static function HeaderValid(first: bv8, second: bv8, length: bv64, limit: bv64): bool {\n      true\n    }\n" + HeaderValidDeclaration);
        var line = Line(source, mode == "unbalanced" ? LengthConjunct + " {{{" : LengthConjunct);
        Assert.Equal("inconclusive", Mutations.Classify(Failure(source, mutant.TargetClause, line), source, mutant).Status);
    }

    // Finding 5. Attributes before the name are allowed, as for methods.
    [Theory]
    [InlineData("{:opaque} ")]
    [InlineData("{:opaque} {:fuel 2} ")]
    public void AttributedNamedPredicateIsFound(string attributes)
    {
        var mutant = Header;
        var source = Edit(Protocol, HeaderValidDeclaration, HeaderValidDeclaration.Replace("function ", "function " + attributes, StringComparison.Ordinal));
        Assert.Equal("detected", Mutations.Classify(Failure(source, mutant.TargetClause, Line(source, LengthConjunct)), source, mutant).Status);
    }

    // Finding 5. A predicate of the same name in another class or module does not count.
    [Theory]
    [InlineData("class")]
    [InlineData("module")]
    public void SameNameElsewhereDoesNotCount(string scope)
    {
        var mutant = Header;
        const string Other = "      length <= limit // the same name in another scope";
        var predicate = "    static function HeaderValid(first: bv8, second: bv8, length: bv64, limit: bv64): bool {\n" + Other + "\n    }\n";
        var source = scope == "class"
            ? Edit(Protocol, "  class NativeCopy {", "  class Other {\n" + predicate + "  }\n  class NativeCopy {")
            : Protocol.TrimEnd() + "\nmodule Other {\n  class Protocol {\n" + predicate + "  }\n}\n";
        Assert.Equal("inconclusive", Mutations.Classify(Failure(source, mutant.TargetClause, Line(source, Other)), source, mutant).Status);
    }

    // Review of #145, findings 1 and 2 (2026-11-12). A brace in a contract is not the body,
    // whatever precedes it and whatever declaration follows the body: a `reads *` or a
    // `|...|` before the body, a body on the signature after a `>`, a field, a `least
    // predicate` or a `@` attribute after it, and a `match` in a contract.
    const string HeaderValidEnd = "      (op < 8 || ((first & 128) != 0 && length <= 125))\n    }\n";
    [Theory]
    [InlineData("      reads *", "    var count: int")]
    [InlineData("      ensures |[first]| == |[second]|", "    least predicate Ready() { true }")]
    [InlineData("      reads *", "    @IsolateAssertions")]
    [InlineData("", "    var count: int")]
    [InlineData("      ensures match first { case 0 => true case _ => true }", "")]
    [InlineData("      ensures match first {\n        case 0 => true\n        case _ => true\n      }", "")]
    public void BraceInAContractOfTheNamedPredicateIsNotTheBody(string contract, string follower)
    {
        var mutant = Header;
        var declaration = contract.Length == 0
            ? HeaderValidDeclaration.Replace("): bool {", "): set<bool> {", StringComparison.Ordinal)
            : HeaderValidDeclaration[..^2] + "\n" + contract + "\n    {";
        var source = Edit(Protocol, HeaderValidDeclaration, declaration);
        if (follower.Length > 0)
            source = Edit(source, HeaderValidEnd, HeaderValidEnd + follower + "\n");
        Assert.Equal("detected", Mutations.Classify(Failure(source, mutant.TargetClause, Line(source, LengthConjunct)), source, mutant).Status);
    }

    // Findings 1 and 2 for the mutated method: Bounds finds DecodeHeader's body after a
    // `decreases *` followed by a field, and after a `match` in its last clause.
    const string DecodeHeaderBody = "    {\n      var marker := second & 127;\n      headerSize :=";
    const string DecodeHeaderEnd = "      status := if HeaderValid(first, second, length, limit) then 2 else 1;\n    }\n";
    [Theory]
    [InlineData("      decreases *", "    var count: int")]
    [InlineData("      ensures match status { case _ => true }", "")]
    [InlineData("      ensures match status {\n        case _ => true\n      }", "    var count: int")]
    public void BraceInAContractOfTheMutatedMethodIsNotTheBody(string contract, string follower)
    {
        var mutant = Header;
        var source = Edit(Protocol, DecodeHeaderBody, contract + "\n" + DecodeHeaderBody);
        if (follower.Length > 0)
            source = Edit(source, DecodeHeaderEnd, DecodeHeaderEnd + follower + "\n");
        Assert.Contains(mutant.After, Mutations.Apply(source, mutant));
        Assert.Equal("detected", Mutations.Classify(Failure(source, mutant.TargetClause, Line(source, LengthConjunct)), source, mutant).Status);
    }

    // Review of #145, finding 3 (2026-11-12). A call qualified with another class names that
    // class's function, not the mutant's class's function of the same name. Qualified with the
    // mutant's own class, it names the mutant's class's function.
    [Theory]
    [InlineData("Other.", "inconclusive")]
    [InlineData("ProtocolCore.Other.", "inconclusive")]
    [InlineData("Protocol.", "detected")]
    [InlineData("ProtocolCore.Protocol.", "detected")]
    public void QualifiedCallNamesTheQualifyingClass(string qualifier, string expected)
    {
        var mutant = Header;
        var clause = mutant.TargetClause.Replace("HeaderValid(", qualifier + "HeaderValid(", StringComparison.Ordinal);
        var predicate = "    static function HeaderValid(first: bv8, second: bv8, length: bv64, limit: bv64): bool {\n      length <= limit\n    }\n";
        var source = Edit(Protocol, "  class NativeCopy {", "  class Other {\n" + predicate + "  }\n  class NativeCopy {");
        source = Edit(source, "      " + mutant.TargetClause + "\n", "      " + clause + "\n");
        var record = Failure(source, clause, Line(source, LengthConjunct));
        Assert.Equal(expected, Mutations.Classify(record, source, mutant with { TargetClause = clause, AllowedClauses = [clause] }).Status);
    }
}
