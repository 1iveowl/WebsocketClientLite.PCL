#!/usr/bin/env python3
"""Run the Close negative proofs in disposable trees with the pinned toolchain."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('requirements', ROOT / 'scripts/validate-requirements.py')
v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def load_catalog(root=ROOT):
    catalog = v.read_json(root / 'verification/mutations/close.json')
    v.require(set(catalog) == {'schema_version', 'mutants'} and catalog['schema_version'] == 1,
              'unsupported catalog shape/version')
    manifest = v.read_json(root / 'verification/requirements.json')
    v.validate(manifest, root)
    requirements = {r['id']: r for r in manifest['requirements']}
    ids = set()
    v.require(isinstance(catalog['mutants'], list) and catalog['mutants'], 'empty catalog')
    for m in catalog['mutants']:
        v.require(set(m) == {'id', 'requirement', 'obligation', 'file', 'symbol', 'before', 'after',
                            'target_clause', 'allowed_clauses', 'classification'}, 'invalid mutant fields')
        v.require(all(isinstance(m[k], str) and m[k] for k in m if k != 'allowed_clauses'), 'empty mutant field')
        v.require(re.fullmatch(r'CLOSE-[0-9]{3}-[a-z-]+', m['id']) and m['id'] not in ids, 'invalid/duplicate mutant ID')
        ids.add(m['id'])
        v.require(m['file'] == 'verification/Protocol.dfy' and m['symbol'] == 'ProtocolCore.Protocol.CloseStep', 'unsupported mutation scope')
        req = requirements.get(m['requirement'])
        v.require(req and any(o['id'] == m['obligation'] and o['kind'] == 'mutation' for o in req['obligations']), 'dangling mutation obligation')
        v.require(any(m['target_clause'] in c['clauses'] for c in req['contracts']), 'target not mapped to requirement')
        header = v.resolve(root, m)
        v.require(isinstance(m['allowed_clauses'], list) and m['target_clause'] in m['allowed_clauses'], 'missing target clause')
        v.require(all(isinstance(c, str) and c.startswith('ensures ') and c in [x.strip() for x in header.splitlines()] for c in m['allowed_clauses']), 'unresolved allowed clause')
        v.require(m['classification'] == 'postcondition', 'unsupported classification')
        apply_mutation(v.source(root, m['file']), m)
    for req in requirements.values():
        for obligation in req['obligations']:
            if obligation['kind'] == 'mutation' and obligation['delivery'] in ('B', 'existing'):
                v.require(any(m['obligation'] == obligation['id'] for m in catalog['mutants']), 'uncovered mutation obligation')
    return catalog


def apply_mutation(source, mutant):
    # Exact, unique, single-line body edits keep diagnostic locations stable and
    # cannot change a precondition/postcondition to manufacture detection.
    start = source.index('{', source.index('static method CloseStep(')) + 1
    end = source.index('\n    }', start)
    before, after = mutant['before'], mutant['after']
    v.require('\n' not in before + after and before != after and source.count(before) == 1,
              'mutation must be a unique, changed single line')
    v.require(before in source[start:end] and all(c not in after for c in '{}'), 'mutation outside CloseStep body')
    return source.replace(before, after, 1)


def execute(command, cwd, directory, budget):
    directory.mkdir(parents=True)
    start = time.monotonic()
    record = {'command': [str(x) for x in command], 'budget_seconds': budget,
              'started_at': datetime.now(timezone.utc).isoformat(), 'exit_code': None,
              'timed_out': False, 'interrupted': False}
    with (directory / 'stdout.log').open('w') as stdout, (directory / 'stderr.log').open('w') as stderr:
        try:
            process = subprocess.Popen(command, cwd=cwd, stdout=stdout, stderr=stderr, start_new_session=True)
            try:
                record['exit_code'] = process.wait(timeout=budget)
            except (subprocess.TimeoutExpired, KeyboardInterrupt) as error:
                record['timed_out'] = isinstance(error, subprocess.TimeoutExpired)
                record['interrupted'] = isinstance(error, KeyboardInterrupt)
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                record['exit_code'] = process.wait()
        except OSError as error:
            record['launch_error'] = str(error)
    record['elapsed_seconds'] = round(time.monotonic() - start, 3)
    record['stdout'] = (directory / 'stdout.log').read_text()
    record['stderr'] = (directory / 'stderr.log').read_text()
    return record


def classify(record, source, mutant=None):
    if record.get('timed_out') or record.get('interrupted') or record.get('launch_error'):
        return 'inconclusive', []
    output = record['stdout'] + '\n' + record['stderr']
    summaries = re.findall(r'Dafny program verifier finished with (\d+) verified, (\d+) errors?', output)
    if len(summaries) != 1 or re.search(r'timed out|timeout|resource limit|inconclusive', output, re.I):
        return 'inconclusive', []
    verified, errors = map(int, summaries[0])
    if record['exit_code'] == 0 and errors == 0 and verified > 0 and 'Error:' not in output:
        return ('survived' if mutant else 'passed'), []
    if not mutant or record['exit_code'] != 4 or errors == 0:
        return 'inconclusive', []
    # Only Dafny 4.11's postcondition diagnostics at this method and its
    # catalogued clauses count. Additional errors anywhere fail closed.
    primary = re.findall(r'^(.+?)\((\d+),(\d+)\): Error: (.+)$', output, re.M)
    related = re.findall(r'^(.+?)\((\d+),(\d+)\): Related location: (.+)$', output, re.M)
    if len(primary) != errors or len(related) != errors or output.count('Error:') != errors:
        return 'inconclusive', []
    lines = source.splitlines()
    body_start = source[:source.index('{', source.index('static method CloseStep('))].count('\n') + 1
    body_end = source[:source.index('\n    }', source.index('static method CloseStep('))].count('\n') + 2
    for file, line, _, message in primary:
        if file != 'verification/Protocol.dfy' or not body_start <= int(line) <= body_end or message != 'a postcondition could not be proved on this return path':
            return 'inconclusive', []
    matched = []
    for file, line, _, message in related:
        if file != 'verification/Protocol.dfy' or not 1 <= int(line) <= len(lines) or message != 'this is the postcondition that could not be proved':
            return 'inconclusive', []
        clause = lines[int(line) - 1].strip()
        if clause not in mutant['allowed_clauses']:
            return 'inconclusive', []
        matched.append(clause)
    return ('detected' if mutant['target_clause'] in matched else 'inconclusive'), matched


def run_suite(catalog, sources, output, command, baseline_budget, mutant_budget, executor=execute):
    report = {'schema_version': 1, 'status': 'failed', 'source_hashes': {k: digest(s.encode()) for k, s in sources.items()},
              'experiments': [{'id': item['id'], 'status': 'not-run'} for item in catalog['mutants']]}
    def save():
        (output / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
    save()
    try:
        for mutant in [None] + catalog['mutants']:
            name = mutant['id'] if mutant else 'baseline'
            experiment = {'id': name, 'status': 'not-run'}
            if mutant:
                experiment = next(e for e in report['experiments'] if e['id'] == name)
            else:
                report['experiments'].insert(0, experiment)
            if mutant:
                experiment.update(requirement=mutant['requirement'], obligation=mutant['obligation'], target_clause=mutant['target_clause'])
            with tempfile.TemporaryDirectory(prefix='close-mutation-') as temporary:
                tree = Path(temporary)
                for file, source in sources.items():
                    path = tree / file
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text(apply_mutation(source, mutant) if mutant and file == mutant['file'] else source)
                experiment['source_hashes'] = {f: digest((tree / f).read_bytes()) for f in sources}
                record = executor(command, tree, output / name, mutant_budget if mutant else baseline_budget)
                experiment.update(record)
                experiment['status'], experiment['matched_clauses'] = classify(record, (tree / 'verification/Protocol.dfy').read_text(), mutant)
            save()
            print(f"{name}: {experiment['status']} ({record['elapsed_seconds']}s)", flush=True)
            if experiment['status'] != ('detected' if mutant else 'passed'):
                return report
        report['status'] = 'passed'
        return report
    finally:
        save()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, help='New evidence directory (must not exist)')
    parser.add_argument('--baseline-budget', type=float, default=180)
    parser.add_argument('--mutant-budget', type=float, default=90)
    parser.add_argument('--verification-time-limit', type=int, default=60,
                        help='Dafny per-obligation limit in seconds (default: 60); wall-clock budgets still apply')
    args = parser.parse_args()
    output = (args.output or ROOT / 'artifacts/close-mutations' / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')).resolve()
    output.mkdir(parents=True, exist_ok=False)
    try:
        v.require(0 < args.baseline_budget < float('inf') and 0 < args.mutant_budget < float('inf'), 'budgets must be positive and finite')
        v.require(args.verification_time_limit > 0, 'verification time limit must be positive')
        catalog = load_catalog()
        setup = execute(['bash', str(ROOT / 'scripts/dafny-toolchain.sh')], ROOT, output / 'toolchain', 180)
        v.require(setup['exit_code'] == 0 and not setup['timed_out'] and not setup['interrupted'], 'toolchain bootstrap failed')
        tool = Path(setup['stdout'].strip()) / 'dafny'
        versions = {}
        for name, executable, expected in [('dafny', tool / 'dafny', '4.11.0'), ('z3', tool / 'z3/bin/z3-4.12.1', '4.12.1')]:
            result = execute([str(executable), '--version'], ROOT, output / name, 15)
            versions[name] = result
            v.require(result['exit_code'] == 0 and expected in result['stdout'], f'{name}: unexpected/missing version')
        (output / 'tool-versions.json').write_text(json.dumps(versions, indent=2) + '\n')
        shutil.copyfile(ROOT / 'verification/mutations/close.json', output / 'catalog.json')
        shutil.copyfile(ROOT / 'verification/requirements.json', output / 'requirements.json')
        sources = {f'verification/{name}': (ROOT / 'verification' / name).read_text() for name in ('Masking.dfy', 'Protocol.dfy')}
        command = [str(tool / 'dafny'), 'verify', 'verification/Masking.dfy', '--verify-included-files',
                   '--allow-external-contracts', '--solver-path', str(tool / 'z3/bin/z3-4.12.1'),
                   '--verification-time-limit', str(args.verification_time_limit), '--cores', '2']
        for file, source in sources.items():
            snapshot = output / 'sources' / file
            snapshot.parent.mkdir(parents=True, exist_ok=True)
            snapshot.write_text(source)
        report = run_suite(catalog, sources, output, command, args.baseline_budget, args.mutant_budget)
        return 0 if report['status'] == 'passed' else 1
    except (ValueError, OSError, KeyError, TypeError, KeyboardInterrupt) as error:
        (output / 'failure.json').write_text(json.dumps({'status': 'inconclusive', 'error': str(error) or 'interrupted'}) + '\n')
        print(f'mutations: {error or "interrupted"}', file=sys.stderr)
        return 1
    finally:
        print(f'Evidence: {output}')


if __name__ == '__main__':
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    sys.exit(main())
