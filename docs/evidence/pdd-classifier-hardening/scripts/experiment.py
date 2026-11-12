"""Runs the Pdd tests in an isolated copy (session plan step S10, 2026-11-11).
usage: python3 -I experiment.py <repo> <commit|worktree> [--add FILE]... [--replace FILE OLD NEW]... [--filter EXPR]
--add copies FILE from the working tree into the copy; --replace edits the copy, and each OLD
must occur exactly once. The copy is its own git repository, because the capture tests read
the source identity from git. The working checkout is not changed."""
import os, subprocess, sys, tempfile
repo, source, args = sys.argv[1], sys.argv[2], sys.argv[3:]
copy = tempfile.mkdtemp(prefix="s10-experiment.")
def run(cmd, **kw): return subprocess.run(cmd, check=True, **kw)
if source == "worktree":
    files = subprocess.run(["git", "-C", repo, "ls-files", "-z", "--cached", "--others", "--exclude-standard"], capture_output=True, check=True).stdout
    run(["tar", "--null", "-T", "-", "-cf", f"{copy}.tar"], input=files, cwd=repo)
    head = subprocess.run(["git", "-C", repo, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    print(f"source: working tree of {repo} at {head}, with these changes:", flush=True)
    run(["git", "-C", repo, "status", "--short"])
else:
    run(["bash", "-c", f"git -C {repo} archive {source} > {copy}.tar"])
    print("source: git archive " + subprocess.run(["git", "-C", repo, "rev-parse", source], capture_output=True, text=True).stdout.strip(), flush=True)
run(["tar", "-xf", f"{copy}.tar", "-C", copy]); os.remove(f"{copy}.tar")
print(f"copy: {copy}", flush=True)
test_filter, i = None, 0
while i < len(args):
    if args[i] == "--add":
        rel = args[i + 1]; os.makedirs(os.path.dirname(f"{copy}/{rel}"), exist_ok=True)
        open(f"{copy}/{rel}", "w").write(open(f"{repo}/{rel}").read()); print(f"added from the working tree: {rel}"); i += 2
    elif args[i] == "--replace":
        rel, old, new = args[i + 1:i + 4]; text = open(f"{copy}/{rel}").read()
        assert text.count(old) == 1, f"{rel}: expected one occurrence of {old!r}, found {text.count(old)}"
        open(f"{copy}/{rel}", "w").write(text.replace(old, new)); print(f"edited {rel}:\n  - {old!r}\n  + {new!r}"); i += 4
    elif args[i] == "--filter":
        test_filter = args[i + 1]; i += 2
    else: sys.exit(f"unknown argument {args[i]}")
sys.stdout.flush()
run(["bash", "-c", "git init -q && git add -A && git -c user.name=copy -c user.email=copy@invalid commit -qm copy"], cwd=copy)
cmd = ["dotnet", "test", "tools/Pdd.Tests/Pdd.Tests.csproj", "-c", "Release"] + (["--filter", test_filter] if test_filter else [])
print("command: " + " ".join(cmd), flush=True)
sys.exit(subprocess.run(cmd, cwd=copy).returncode)
