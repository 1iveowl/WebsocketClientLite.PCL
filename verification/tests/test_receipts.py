"""Receipt fixtures exercise the fail-closed gate without invoking expensive tools."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('receipt', ROOT / 'scripts/pdd-receipt.py')
r = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r)


class ReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.source = {'tested_commit': 'merge', 'pr_source_head': 'head', 'dirty_worktree': False,
                       'source_hashes': {'verification/Protocol.dfy': 'a', 'verification/Masking.dfy': 'b'}}
        self.manifest = json.loads((ROOT / 'verification/requirements.json').read_text())
        self.catalog = json.loads((ROOT / 'verification/mutations/close.json').read_text())['mutants']
        for job in ('protocol', 'tests'):
            directory = self.directory / job
            directory.mkdir()
            runs = {}
            for name, command in r.commands(job, directory):
                (directory / f'{name}.log').write_text('Dafny program verifier finished with 41 verified, 0 errors')
                runs[name] = {'status': 'passed', 'exit_code': 0, 'command': command,
                              'started_at': r.now(), 'finished_at': r.now(), 'log': f'{name}.log'}
            r.save(directory / 'job.json', {'schema_version': 1, 'job': job, 'source': self.source,
                    'source_after': self.source, 'ci': r.ci_identity(), 'runs': runs, 'tools': {}})
        coverage = self.directory / 'tests/coverage'
        coverage.mkdir()
        (coverage / 'coverage.cobertura.xml').write_text('<coverage line-rate="0.75"/>')
        mutation_dir = self.directory / 'protocol/mutations'
        mutation_dir.mkdir()
        r.save(mutation_dir / 'tool-versions.json', {'dafny': {'stdout': '4.11.0'}, 'z3': {'stdout': '4.12.1'}})
        r.save(mutation_dir / 'results.json', {'status': 'passed', 'source_hashes': self.source['source_hashes'],
               'experiments': [{'id': 'baseline', 'status': 'passed', 'exit_code': 0}] +
               [{'id': m['id'], 'status': 'detected', 'exit_code': 4, 'matched_clauses': [m['target_clause']]}
                for m in self.catalog]})
        names = {ref['symbol'] for req in self.manifest['requirements'] for o in req['obligations']
                 if o['kind'] == 'test' for ref in o['references']}
        names.add('WebsocketClientLiteTest.RuntimeEvidenceTests.RecordHostRuntime')
        for tfm in r.TFMS:
            path = self.directory / 'tests/tests' / tfm
            path.mkdir(parents=True)
            r.save(path / 'run.json', {'status': 'passed', 'exit_code': 0,
                   'started_at': r.now(), 'finished_at': r.now(), 'command': ['dotnet', 'test', '-p:LibTfm=' + tfm]})
            (path / 'output.log').write_text('Test output')
            root = ET.Element('TestRun', xmlns=r.NS['t'])
            definitions = ET.SubElement(root, 'TestDefinitions')
            results = ET.SubElement(root, 'Results')
            for i, name in enumerate(sorted(names)):
                test = ET.SubElement(definitions, 'UnitTest', id=str(i))
                cls, method = name.rsplit('.', 1)
                ET.SubElement(test, 'TestMethod', className=cls, name=method)
                result = ET.SubElement(results, 'UnitTestResult', testId=str(i), outcome='Passed')
                if method == 'RecordHostRuntime':
                    ET.SubElement(ET.SubElement(result, 'Output'), 'StdOut').text = 'PDD_HOST_RUNTIME=.NET 10.0.0'
            ET.ElementTree(root).write(path / 'results.trx')

    def receipt(self):
        return r.aggregate(self.directory, self.source)

    def edit(self, path, change):
        file = self.directory / path
        value = json.loads(file.read_text())
        change(value)
        r.save(file, value)

    def test_success_keeps_pending_tests_reviews_and_stryker_unsatisfied(self):
        receipt = self.receipt()
        self.assertEqual('passed', receipt['gate']['status'])
        self.assertFalse(any(q['satisfied'] for q in receipt['requirements']))
        self.assertTrue(all(q['status'] == 'not-run' for q in receipt['review_evidence']))
        controlled = [o for q in receipt['requirements'] for o in q['machine_evidence'] if o['delivery'] == 'E']
        self.assertEqual(2, len(controlled))
        self.assertTrue(all(o['status'] == 'not-run' for o in controlled))
        self.assertEqual('not-run', receipt['stryker']['status'])
        self.assertEqual(5, len(receipt['framework_matrix']))
        self.assertEqual({'.NET 10.0.0'}, {x['host_runtime'] for x in receipt['framework_matrix'].values()})

    def test_missing_job_fails(self):
        (self.directory / 'protocol/job.json').unlink()
        self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_revision_and_hash_mismatch_are_not_combined(self):
        for field, value in [('tested_commit', 'other'), ('pr_source_head', 'other'),
                             ('source_hashes', {}), ('dirty_worktree', True)]:
            with self.subTest(field=field):
                changed = copy.deepcopy(self.source)
                changed[field] = value
                self.edit('protocol/job.json', lambda d: d.update(source=changed))
                receipt = self.receipt()
                self.assertEqual('failed', receipt['gate']['status'])
                self.assertNotIn('protocol', receipt['jobs'])

    def test_different_ci_attempt_rejected(self):
        self.edit('tests/job.json', lambda d: d['ci'].update(GITHUB_RUN_ATTEMPT='other'))
        self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_failed_skipped_and_interrupted_commands(self):
        for status, code in [('failed', 1), ('skipped', None), ('not-run', None), ('passed', 1)]:
            self.edit('protocol/job.json', lambda d: d['runs']['proof'].update(status=status, exit_code=code))
            self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_missing_or_low_coverage_evidence(self):
        path = self.directory / 'tests/coverage/coverage.cobertura.xml'
        path.write_text('<coverage line-rate="0.69"/>')
        self.assertEqual('failed', self.receipt()['gate']['status'])
        path.unlink()
        self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_empty_proof_log_fails_even_with_zero_exit(self):
        (self.directory / 'protocol/proof.log').write_text('')
        self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_malformed_job_is_rejected_but_preserved(self):
        (self.directory / 'tests/job.json').write_text('[]')
        receipt = self.receipt()
        self.assertEqual('failed', receipt['gate']['status'])
        self.assertIn('tests', receipt['rejected_jobs'])

    def test_missing_log_or_coverage_gate_fails(self):
        (self.directory / 'tests/coverage-gate.log').unlink()
        self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_missing_target_and_missing_host_fail(self):
        path = self.directory / 'tests/tests/net8.0/results.trx'
        original = path.read_text()
        path.write_text(original.replace('PDD_HOST_RUNTIME=', 'missing='))
        self.assertEqual('failed', self.receipt()['gate']['status'])
        path.unlink()
        self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_failed_and_skipped_named_test(self):
        path = self.directory / 'tests/tests/net8.0/results.trx'
        original = path.read_text()
        for outcome in ('Failed', 'NotExecuted'):
            path.write_text(original.replace('outcome="Passed"', f'outcome="{outcome}"'))
            self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_mutation_missing_target_and_mismatched_source(self):
        self.edit('protocol/mutations/results.json', lambda d: d['experiments'].pop())
        self.assertEqual('failed', self.receipt()['gate']['status'])
        self.edit('protocol/mutations/results.json', lambda d: d.update(source_hashes={}))
        self.assertEqual('failed', self.receipt()['gate']['status'])

    def test_capture_real_failure_skips_following_and_refuses_stale_directory(self):
        output = self.directory / 'capture'
        with patch.object(r, 'commands', return_value=[('fail', ['python3', '-c', 'import sys;sys.exit(7)']),
                                                       ('later', ['python3', '-c', 'pass'])]):
            self.assertEqual(1, r.capture('tests', output))
            result = json.loads((output / 'job.json').read_text())
            self.assertEqual(7, result['runs']['fail']['exit_code'])
            self.assertEqual('skipped', result['runs']['later']['status'])
            with self.assertRaises(FileExistsError):
                r.capture('tests', output)


if __name__ == '__main__':
    unittest.main()
