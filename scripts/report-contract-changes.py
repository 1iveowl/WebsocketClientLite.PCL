#!/usr/bin/env python3
"""Report committed changes from the actual target's merge base; no semantic verdict."""
import argparse
import difflib
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def git(root, *args):
    run = subprocess.run(['git', '-C', str(root), *args], capture_output=True,
                         text=True, encoding='utf-8', errors='replace', timeout=60)
    if run.returncode:
        raise ValueError(run.stderr.strip() or f'git {args[0]} failed')
    return run.stdout


def fence(text, language='diff'):
    # Source text can itself contain Markdown fences.
    marker = '`' * max(3, 1 + max((len(m[0]) for m in re.finditer(r'`+', text)), default=0))
    return f'{marker}{language}\n{text.rstrip()}\n{marker}\n'


def report(root, target, head):
    target_sha = git(root, 'rev-parse', '--verify', '--end-of-options', target + '^{commit}').strip()
    head_sha = git(root, 'rev-parse', '--verify', '--end-of-options', head + '^{commit}').strip()
    bases = git(root, 'merge-base', '--all', target_sha, head_sha).splitlines()
    if len(bases) != 1:
        raise ValueError('comparison requires exactly one merge base')
    base = bases[0]
    def paths(revision):
        return set(git(root, 'ls-tree', '-r', '--name-only', '-z', revision).rstrip('\0').split('\0'))
    old_paths, new_paths = paths(base), paths(head_sha)
    def content(revision, name, names):
        return git(root, 'show', f'{revision}:{name}') if name in names else ''
    def diff(before, after, name):
        return ''.join(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
                                          fromfile=f'base/{name}', tofile=f'head/{name}'))
    sections = []
    manifest = 'verification/requirements.json'
    def intents(revision, names):
        raw = content(revision, manifest, names)
        if not raw:
            return ''
        data = json.loads(raw)
        return json.dumps([{'id': r['id'], 'intent': r['intent']} for r in data['requirements']],
                          indent=2, ensure_ascii=False) + '\n'
    sections.append(('Requirement intent', diff(intents(base, old_paths), intents(head_sha, new_paths), manifest)))
    for name, title in [('verification/boundaries.json', 'Boundary register'),
                        ('verification/trusted-boundaries.md', 'Machine-compared external-contract audit')]:
        sections.append((title, diff(content(base, name, old_paths), content(head_sha, name, new_paths), name)))
    for name in sorted(old_paths | new_paths):
        if not name.endswith('.dfy'):
            continue
        before, after = content(base, name, old_paths), content(head_sha, name, new_paths)
        if before == after:
            continue
        # Intentionally retain the entire source diff: helper bodies, multiline
        # clauses, attributes, types and external declarations all affect meaning.
        sections.append((f'Dafny contracts, specification helpers and external declarations: {name}',
                         diff(before, after, name)))
    full = git(root, 'diff', '--no-ext-diff', '--no-textconv', '--no-renames', '--binary',
               base, head_sha, '--', '.')
    lines = ['# Contract-change review report', '',
             f'- Target commit: `{target_sha}`', f'- Compared head: `{head_sha}`',
             f'- Merge base: `{base}`', '',
             ('**Semantic review required.**' if full else 'No committed changes in this comparison.'), '',
             'This is a textual comparison, not a proof of equivalence or a weakening detector. '
             'Review stronger preconditions, weaker postconditions, helper definitions and expanded trust. '
             'Normal PR review must record the accepted decision and reviewed head revision; '
             'a committed approval flag is not evidence.', '',
             'Only committed trees are compared. Uncommitted/untracked files are excluded. '
             'The full source diff includes changes outside the summaries. No verifier or tests run here.', '']
    for title, changes in sections:
        lines += [f'## {title}', '', fence(changes) if changes else 'No textual changes.', '']
    lines += ['## Full source diff', '', fence(full) if full else 'No textual changes.', '']
    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', required=True, help='actual PR target ref, fetched locally; no default branch')
    parser.add_argument('--head', default='HEAD', help='committed PR source head (not a synthetic merge)')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        result = report(ROOT, args.target, args.head)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(result, encoding='utf-8')
    except (ValueError, KeyError, TypeError, OSError, subprocess.TimeoutExpired) as error:
        print(f'contract report: {error}', file=sys.stderr)
        return 1
    print(f'Contract-change report: {args.output} (review aid only).')
    return 0


if __name__ == '__main__':
    sys.exit(main())
