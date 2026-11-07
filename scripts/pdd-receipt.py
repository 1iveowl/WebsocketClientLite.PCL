#!/usr/bin/env python3
"""Capture CI commands and aggregate revision-bound PDD evidence (not attestations)."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TFMS = ['netstandard2.0', 'netstandard2.1', 'net8.0', 'net9.0', 'net10.0']
PROJECT = 'src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def now():
    return datetime.now(timezone.utc).isoformat()


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT).decode().strip()


def identity():
    paths = git('ls-files', '--cached', '--others', '--exclude-standard', 'src', 'verification',
                'scripts', '.github', '.config', 'global.json', 'Directory.Build.props',
                'Directory.Build.targets', 'NuGet.config', 'GitVersion.yml', 'test-all-tfms.sh').splitlines()
    return {'tested_commit': git('rev-parse', 'HEAD'),
            'pr_source_head': os.environ.get('PDD_PR_HEAD') or None,
            'dirty_worktree': bool(git('status', '--porcelain')),
            'source_hashes': {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest()
                              if (ROOT / p).is_file() else None for p in sorted(set(paths))}}


def ci_identity():
    return {k: os.environ.get(k) for k in ('GITHUB_SERVER_URL', 'GITHUB_REPOSITORY',
            'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_EVENT_NAME', 'GITHUB_JOB', 'GITHUB_SHA')}


def save(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n')


def commands(job, output):
    if job == 'protocol':
        return [('manifest', ['python3', 'scripts/validate-requirements.py']),
                ('fixtures', ['python3', '-m', 'unittest', 'discover', '-s', 'verification/tests', '-v']),
                ('proof', ['./scripts/dafny-masking.sh', '--check']),
                ('mutations', ['python3', 'scripts/run-close-mutations.py', '--output', str(output / 'mutations')])]
    return [('restore', ['dotnet', 'restore', 'src/main/WebsocketClientLite.sln']),
            ('build', ['dotnet', 'build', 'src/main/WebsocketClientLite.sln', '--configuration', 'Release', '--no-restore']),
            ('matrix', ['./test-all-tfms.sh', 'Release']),
            ('coverage-run', ['dotnet', 'test', PROJECT, '--configuration', 'Release',
                              '--collect:XPlat Code Coverage', '--results-directory', str(output / 'coverage')]),
            ('coverage-gate', ['./scripts/check-coverage.sh', str(output / 'coverage'), '70'])]


def execute(command, output, env=None):
    record = {'command': command, 'started_at': now(), 'finished_at': None,
              'status': 'not-run', 'exit_code': None, 'log': 'output.log'}
    save(output / 'run.json', record)
    with (output / 'output.log').open('w') as log:
        try:
            result = subprocess.run(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
            record['exit_code'] = result.returncode
        except OSError as error:
            log.write(str(error))
    record.update(finished_at=now(), status='passed' if record['exit_code'] == 0 else 'failed')
    save(output / 'run.json', record)
    return record


def test_target(tfm, output):
    output.mkdir(parents=True, exist_ok=False)
    record = execute(['dotnet', 'test', PROJECT, '-c', os.environ.get('PDD_TEST_CONFIG', 'Release'),
                      '-p:LibTfm=' + tfm, '--nologo', '--logger', 'trx;LogFileName=results.trx',
                      '--results-directory', str(output)], output)
    print((output / 'output.log').read_text(), end='')
    return int(record['status'] != 'passed')


def capture(job, output):
    output.mkdir(parents=True, exist_ok=False)  # Never reuse stale results.
    report = {'schema_version': 1, 'job': job, 'source': identity(), 'ci': ci_identity(),
              'started_at': now(), 'tools': {'python': platform.python_version()}, 'runs': {}}
    for name, command in commands(job, output):
        report['runs'][name] = {'status': 'not-run', 'command': command, 'exit_code': None,
                                'started_at': None, 'finished_at': None, 'log': f'{name}.log'}
    save(output / 'job.json', report)
    try:
        version = subprocess.run(['dotnet', '--info'], capture_output=True, text=True)
        report['tools']['dotnet'] = {'command': ['dotnet', '--info'], 'exit_code': version.returncode,
                                     'output': version.stdout + version.stderr}
    except OSError as error:
        report['tools']['dotnet'] = {'command': ['dotnet', '--info'], 'exit_code': None, 'error': str(error)}
    failed = False
    for name, command in commands(job, output):
        run = report['runs'][name]
        if failed:
            run['status'] = 'skipped'
        else:
            run['started_at'] = now()
            save(output / 'job.json', report)
            with (output / run['log']).open('w') as log:
                try:
                    env = dict(os.environ, PDD_TEST_RESULTS=str(output / 'tests'))
                    result = subprocess.run(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
                    run['exit_code'] = result.returncode
                except OSError as error:
                    log.write(str(error))
                run['status'] = 'passed' if run['exit_code'] == 0 else 'failed'
            print(f'{name}: {run["status"]}', flush=True)
            run['finished_at'] = now()
            failed = run['status'] != 'passed'
        save(output / 'job.json', report)
    report['finished_at'] = now()
    report['source_after'] = identity()
    save(output / 'job.json', report)
    return int(failed)


def test_results(directory):
    result = {}
    for tfm in TFMS:
        files = list((directory / 'tests' / tfm).glob('*.trx'))
        if len(files) != 1:
            result[tfm] = {'status': 'not-run', 'tests': {}, 'host_runtime': None}
            continue
        tree = ET.parse(files[0])
        definitions = {u.attrib['id']: u.find('t:TestMethod', NS) for u in tree.findall('.//t:UnitTest', NS)}
        tests = {}
        runtime = None
        for item in tree.findall('.//t:UnitTestResult', NS):
            method = definitions.get(item.attrib['testId'])
            if method is None:
                raise ValueError('TRX result has no test definition')
            name = method.attrib['className'].split(',')[0] + '.' + method.attrib['name']
            status = {'Passed': 'passed', 'NotExecuted': 'skipped'}.get(item.attrib['outcome'], 'failed')
            severity = {'passed': 0, 'skipped': 1, 'not-run': 2, 'failed': 3}
            tests[name] = max(status, tests.get(name, 'passed'), key=severity.get)
            if name == 'WebsocketClientLiteTest.RuntimeEvidenceTests.RecordHostRuntime' and status == 'passed':
                match = re.search(r'PDD_HOST_RUNTIME=(.+)', ''.join(item.itertext()))
                runtime = match.group(1).strip() if match else None
        run = json.loads((directory / 'tests' / tfm / 'run.json').read_text())
        valid_run = (run.get('status') == 'passed' and run.get('exit_code') == 0 and
                     run.get('started_at') and run.get('finished_at') and
                     '-p:LibTfm=' + tfm in run.get('command', []) and
                     (directory / 'tests' / tfm / 'output.log').is_file())
        result[tfm] = {'status': 'passed' if valid_run and tests and all(x == 'passed' for x in tests.values()) and runtime else 'failed',
                       'execution': run,
                       'tests': tests, 'host_runtime': runtime, 'trx': str(files[0].relative_to(directory))}
    return result


def aggregate(directory, expected):
    receipt = {'schema_version': 1, 'created_at': now(), 'source': expected, 'ci': ci_identity(),
               'jobs': {}, 'rejected_jobs': {}, 'errors': [], 'requirements': [], 'review_evidence': [],
               'stryker': {'status': 'not-run', 'delivery': 'E', 'reason': 'Separate C# mutation evidence pending increment E'}}
    errors = receipt['errors']
    for job in ('protocol', 'tests'):
        path = directory / job / 'job.json'
        if not path.exists():
            errors.append(f'Missing {job} job evidence')
            continue
        data = None
        try:
            data = json.loads(path.read_text())
            if data['schema_version'] != 1 or data['job'] != job or data['source'] != expected or data.get('source_after') != expected:
                raise ValueError('source identity mismatch or incomplete run')
            for key in ('GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_REPOSITORY', 'GITHUB_EVENT_NAME', 'GITHUB_SHA'):
                if data['ci'].get(key) != receipt['ci'].get(key):
                    raise ValueError('CI identity mismatch')
            receipt['jobs'][job] = data
            for name, _ in commands(job, directory / job):
                run = data['runs'].get(name, {})
                if (run.get('status') != 'passed' or run.get('exit_code') != 0 or
                        not run.get('started_at') or not run.get('finished_at') or
                        not (directory / job / run.get('log', 'missing')).is_file()):
                    errors.append(f'{job}/{name}: missing or unsuccessful command evidence')
        except (ValueError, KeyError, TypeError, AttributeError) as error:
            receipt['jobs'].pop(job, None)
            receipt['rejected_jobs'][job] = {'reason': str(error), 'partial_evidence': data}
            errors.append(f'{job}: {error}')
    # A zero process exit alone must not invent a successful verification result.
    proof_run = receipt['jobs'].get('protocol', {}).get('runs', {}).get('proof', {})
    if proof_run.get('status') == 'passed':
        log = directory / 'protocol/proof.log'
        summaries = re.findall(r'Dafny program verifier finished with (\d+) verified, (\d+) errors?',
                               log.read_text() if log.is_file() else '')
        if not summaries or any(int(n) == 0 or int(e) != 0 for n, e in summaries):
            proof_run['status'] = 'failed'
            errors.append('Missing or failed verifier summary')
    matrix = {}
    mutations = {}
    try:
        if 'tests' in receipt['jobs']:
            matrix = test_results(directory / 'tests')
            if any(r['status'] != 'passed' for r in matrix.values()):
                errors.append('Incomplete or unsuccessful framework matrix/runtime evidence')
        if 'protocol' in receipt['jobs']:
            mutations = json.loads((directory / 'protocol/mutations/results.json').read_text())
            versions = json.loads((directory / 'protocol/mutations/tool-versions.json').read_text())
            receipt['jobs']['protocol']['tools']['verifier'] = versions
            if mutations['source_hashes'] != {p: expected['source_hashes'][p] for p in mutations['source_hashes']}:
                raise ValueError('mutation source hashes mismatch')
            if set(mutations['source_hashes']) != {'verification/Masking.dfy', 'verification/Protocol.dfy'}:
                raise ValueError('missing mutation source hashes')
            if mutations['status'] != 'passed':
                errors.append('Mutation suite failed')
    except (OSError, ValueError, KeyError, TypeError, AttributeError, ET.ParseError) as error:
        errors.append(f'Invalid raw evidence: {error}')
        mutations = {}
    receipt['coverage'] = {'status': 'not-run', 'reports': []}
    if 'tests' in receipt['jobs']:
        try:
            reports = list((directory / 'tests/coverage').glob('**/coverage.cobertura.xml'))
            if not reports:
                raise ValueError('missing coverage report')
            for path in reports:
                rate = float(ET.parse(path).getroot().attrib['line-rate'])
                if not 0.70 <= rate <= 1:
                    raise ValueError('coverage below 70% or invalid rate')
                receipt['coverage']['reports'].append({'path': str(path.relative_to(directory)), 'line_rate': rate})
            receipt['coverage']['status'] = 'passed'
        except (OSError, ValueError, KeyError, ET.ParseError) as error:
            receipt['coverage']['status'] = 'failed'
            errors.append(f'Coverage: {error}')
    receipt['framework_matrix'] = matrix
    receipt['dafny_mutations'] = mutations
    manifest = json.loads((ROOT / 'verification/requirements.json').read_text())
    catalog = json.loads((ROOT / 'verification/mutations/close.json').read_text())['mutants']
    for requirement in manifest['requirements']:
        obligations = []
        for obligation in requirement['obligations']:
            item = {**obligation, 'status': 'not-run', 'evidence': []}
            kind = item['kind']
            if kind == 'review':
                item['reason'] = 'Acceptance must be recorded against the revision in PR review; textual reports are not approval.'
                receipt['review_evidence'].append(item)
            elif item['delivery'] == 'E':
                item['reason'] = 'Pending controlled tests in increment E'
            elif kind == 'proof':
                item['status'] = receipt['jobs'].get('protocol', {}).get('runs', {}).get('proof', {}).get('status', 'not-run')
                item['evidence'] = ['protocol/proof.log']
            elif kind == 'mutation':
                required = [m for m in catalog if m['obligation'] == item['id']]
                experiments = {e['id']: e for e in mutations.get('experiments', [])}
                baseline = experiments.get('baseline', {})
                ok = baseline.get('status') == 'passed' and baseline.get('exit_code') == 0
                ok = ok and all(experiments.get(m['id'], {}).get('status') == 'detected' and
                                experiments[m['id']].get('exit_code') == 4 and
                                m['target_clause'] in experiments[m['id']].get('matched_clauses', []) for m in required)
                item['status'] = 'passed' if required and ok else ('failed' if mutations else 'not-run')
                item['evidence'] = ['protocol/mutations/results.json']
            elif kind == 'test':
                statuses = [matrix.get(tfm, {}).get('tests', {}).get(ref['symbol'], 'not-run')
                            for tfm in TFMS for ref in item['references']]
                item['status'] = ('failed' if 'failed' in statuses else 'not-run' if 'not-run' in statuses
                                  else 'skipped' if 'skipped' in statuses else 'passed') if statuses else 'not-run'
                item['evidence'] = [f'tests/tests/{tfm}/results.trx' for tfm in TFMS]
            if kind != 'review' and item['delivery'] != 'E' and item['status'] != 'passed':
                errors.append(f'{item["id"]}: {item["status"]}')
            obligations.append(item)
        receipt['requirements'].append({'id': requirement['id'], 'satisfied': all(o['status'] == 'passed' for o in obligations),
                                         'machine_evidence': [o for o in obligations if o['kind'] != 'review']})
    receipt['gate'] = {'status': 'failed' if errors else 'passed',
                       'scope': 'Existing machine obligations and CI gates; excludes pending E and PR review, which remain unsatisfied.'}
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['capture', 'aggregate', 'test-target'])
    parser.add_argument('--tfm', choices=TFMS)
    parser.add_argument('--job', choices=['protocol', 'tests'])
    parser.add_argument('--output', type=Path, default=Path('artifacts/pdd'))
    args = parser.parse_args()
    output = args.output.resolve()
    if args.action == 'test-target':
        if not args.tfm:
            parser.error('test-target requires --tfm')
        return test_target(args.tfm, output)
    if args.action == 'capture':
        if not args.job:
            parser.error('capture requires --job')
        return capture(args.job, output)
    output.mkdir(parents=True, exist_ok=True)
    receipt = aggregate(output, identity())
    save(output / 'receipt.json', receipt)
    lines = ['# PDD verification receipt', '', f'Machine evidence gate: **{receipt["gate"]["status"]}**',
             '', receipt['gate']['scope'], '', f'Tested commit: `{receipt["source"]["tested_commit"]}`',
             f'PR source head: `{receipt["source"]["pr_source_head"]}`', '',
             'Five library builds use the actual hosts recorded below; they are not five independently tested runtimes.', '']
    for tfm, result in receipt['framework_matrix'].items():
        lines.append(f'- {tfm}: {result["status"]}; host: {result["host_runtime"]}')
    lines += ['', '| Obligation | Status |', '| --- | --- |']
    for req in receipt['requirements']:
        for item in req['machine_evidence']:
            links = ', '.join(f'[{p}]({p})' for p in item['evidence'])
            lines.append(f'| {item["id"]} | {item["status"]} {links} |')
    lines += ['', 'Review evidence is separate and not-run; no requirement is claimed satisfied without it.',
              'Stryker and controlled schedule evidence: not-run (increment E).', '',
              'Receipts are traceability records, not signed attestations.', '']
    lines += [f'- {e}' for e in receipt['errors']]
    summary = '\n'.join(lines) + '\n'
    (output / 'summary.md').write_text(summary)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as stream:
            stream.write(summary)
    print(summary)
    return int(receipt['gate']['status'] != 'passed')


if __name__ == '__main__':
    sys.exit(main())
