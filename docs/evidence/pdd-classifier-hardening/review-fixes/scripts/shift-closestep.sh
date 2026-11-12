#!/usr/bin/env bash
# usage: shift-closestep.sh <commit-or-"worktree"> <shift|noshift>
# Copies the source into a fresh directory (git archive of a commit, or the working
# tree's tracked and untracked non-ignored files), adds one comment line directly above
# the comment block of CloseStep in verification/Protocol.dfy, and runs the Pdd tests
# in that copy. The working checkout is not touched.
set -u
source_ref="$1"; mode="$2"
repo=$(git rev-parse --show-toplevel)
copy=$(mktemp -d /tmp/s10-shift.XXXXXX)
if [ "$source_ref" = worktree ]; then
  (cd "$repo" && git ls-files -z --cached --others --exclude-standard | tar --null -T - -cf -) | tar -xf - -C "$copy"
  echo "source: working tree of $repo at $(git -C "$repo" rev-parse HEAD), status:"; git -C "$repo" status --short
else
  git -C "$repo" archive "$source_ref" | tar -xf - -C "$copy"
  echo "source: git archive $(git -C "$repo" rev-parse "$source_ref")"
fi
echo "copy: $copy"
# The capture tests read the source identity from git, so the copy is its own repository.
(cd "$copy" && git init -q && git add -A && git -c user.name=copy -c user.email=copy@invalid commit -qm copy) && echo "copy is a git repository with one commit"
if [ "$mode" = noshift ]; then echo "mode: noshift (control, no line added)"; cd "$copy" && exec dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release 2>&1; fi
line=$(grep -n 'static method CloseStep(' "$copy/verification/Protocol.dfy" | cut -d: -f1)
block=$line
while sed -n "$((block-1))p" "$copy/verification/Protocol.dfy" | grep -q '^\s*//'; do block=$((block-1)); done
sed -i "${block}i\\    // Added by the S10 line-shift experiment: one comment line above CloseStep." "$copy/verification/Protocol.dfy"
echo "inserted one comment line at line $block (CloseStep declaration moves from $line to $((line+1)))"
diff -u <(git -C "$repo" show "$( [ "$source_ref" = worktree ] && echo HEAD || echo "$source_ref" ):verification/Protocol.dfy") "$copy/verification/Protocol.dfy"
cd "$copy" && dotnet test tools/Pdd.Tests/Pdd.Tests.csproj -c Release 2>&1
