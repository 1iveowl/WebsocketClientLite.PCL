using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pdd;

public static class ContractReport
{
    public static Task<string> Git(string root, params string[] args) => Processes.Output(["git", "-C", root, .. args], root);
    public static string Fence(string text, string language = "diff")
    {
        var length = Regex.Matches(text, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max();
        var marker = new string('`', Math.Max(3, length + 1));
        return $"{marker}{language}\n{text.TrimEnd()}\n{marker}\n";
    }
    public static async Task<string> Report(string root, string target, string head, CancellationToken cancellation = default)
    {
        Task<string> RunGit(params string[] args) => Processes.Output(["git", "-C", root, .. args], root, cancellation);
        var targetSha = (await RunGit("rev-parse", "--verify", "--end-of-options", target + "^{commit}")).Trim();
        var headSha = (await RunGit("rev-parse", "--verify", "--end-of-options", head + "^{commit}")).Trim();
        var bases = (await RunGit("merge-base", "--all", targetSha, headSha)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Data.Require(bases.Length == 1, "comparison requires exactly one merge base");
        var baseline = bases[0];
        async Task<HashSet<string>> Paths(string revision) => (await RunGit("ls-tree", "-r", "--name-only", "-z", revision)).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var oldPaths = await Paths(baseline);
        var newPaths = await Paths(headSha);
        async Task<string> Content(string revision, string name, HashSet<string> paths) => paths.Contains(name) ? await RunGit("show", $"{revision}:{name}") : "";
        async Task<string> Diff(string name) => await RunGit("diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--binary", baseline, headSha, "--", name);
        async Task<string> Intents(string revision, HashSet<string> paths)
        {
            var raw = await Content(revision, "verification/requirements.json", paths);
            if (raw.Length == 0)
                return "";
            var data = Data.Parse(raw);
            Data.Require(data["requirements"] is JsonArray, "malformed requirements");
            var intents = data["requirements"]!.AsArray().Select(r => new { id = r!["id"]!.GetValue<string>(), intent = r["intent"]!.GetValue<string>() }).ToArray();
            return Data.Node(intents).ToJsonString(Data.Options) + "\n";
        }
        var before = await Intents(baseline, oldPaths);
        var after = await Intents(headSha, newPaths);
        // Git supplies unified diffs; do not maintain another diff algorithm.
        var intentDiff = "";
        if (before != after)
        {
            using var temporary = new TemporaryTree();
            var old = Path.Combine(temporary.Path, "base.json");
            var next = Path.Combine(temporary.Path, "head.json");
            File.WriteAllText(old, before);
            File.WriteAllText(next, after);
            var result = await Processes.Run(["git", "diff", "--no-index", "--no-ext-diff", "--no-textconv", "--", old, next], root, Path.Combine(temporary.Path, "logs"), 60, cancellation);
            Data.Require(result.ExitCode == 1 && !result.TimedOut && !result.Interrupted, "intent comparison failed");
            intentDiff = result.Stdout.Replace(old, "base/verification/requirements.json").Replace(next, "head/verification/requirements.json");
        }
        var sections = new List<(string, string)> { ("Requirement intent", intentDiff) };
        foreach (var (name, title) in new[] { ("verification/boundaries.json", "Boundary register"), ("verification/trusted-boundaries.md", "Machine-compared external-contract audit") })
            sections.Add((title, await Diff(name)));
        foreach (var name in oldPaths.Union(newPaths).Where(n => n.EndsWith(".dfy")).Order(StringComparer.Ordinal))
        {
            var diff = await Diff(name);
            if (diff.Length > 0)
                sections.Add(($"Dafny contracts, specification helpers and external declarations: {name}", diff));
        }
        var full = await Diff(".");
        var lines = new List<string> { "# Contract-change review report", "", $"- Target commit: `{targetSha}`", $"- Compared head: `{headSha}`", $"- Merge base: `{baseline}`", "", full.Length > 0 ? "**Semantic review required.**" : "No committed changes in this comparison.", "", "This is a textual comparison, not a proof of equivalence or a weakening detector. Review stronger preconditions, weaker postconditions, helper definitions and expanded trust. Normal PR review must record the accepted decision and reviewed head revision; a committed approval flag is not evidence.", "", "Only committed trees are compared. Uncommitted/untracked files are excluded. The full source diff includes changes outside the summaries. No verifier or tests run here.", "" };
        foreach (var (title, changes) in sections)
            lines.AddRange([$"## {title}", "", changes.Length > 0 ? Fence(changes) : "No textual changes.", ""]);
        lines.AddRange(["## Full source diff", "", full.Length > 0 ? Fence(full) : "No textual changes.", ""]);
        return string.Join('\n', lines);
    }
}
