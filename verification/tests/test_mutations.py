"""Mutation gate failure modes; no installed verifier needed for these tests."""
import copy
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('mutations', ROOT / 'scripts/run-close-mutations.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class MutationTests(unittest.TestCase):
    def setUp(self):
        self.catalog = m.load_catalog()
        self.mutant = self.catalog['mutants'][0]
        self.source = (ROOT / 'verification/Protocol.dfy').read_text()
        self.sources = {f'verification/{f}': (ROOT / 'verification' / f).read_text() for f in ('Masking.dfy', 'Protocol.dfy')}

    def record(self, clause=None):
        clause = clause or self.mutant['target_clause']
        line = self.source.splitlines().index('      ' + clause) + 1
        return {'exit_code': 4, 'stdout':
                'verification/Protocol.dfy(153,4): Error: a postcondition could not be proved on this return path\n'
                f'verification/Protocol.dfy({line},22): Related location: this is the postcondition that could not be proved\n'
                'Dafny program verifier finished with 40 verified, 1 error\n',
                'stderr': '', 'elapsed_seconds': 0.1}

    def test_targeted_diagnostic_required(self):
        self.assertEqual(m.classify(self.record(), self.source, self.mutant)[0], 'detected')
        cases = []
        for key, value in [('exit_code', 1), ('timed_out', True), ('interrupted', True), ('launch_error', 'missing')]:
            record = self.record(); record[key] = value; cases.append(record)
        for old, new in [('(153,4)', '(170,4)'), ('Protocol.dfy', 'Masking.dfy'),
                         ('a postcondition could not be proved on this return path', 'assertion might not hold'),
                         ('1 error', '2 errors'), ('1 error', '1 error, 1 timed out')]:
            record = self.record(); record['stdout'] = record['stdout'].replace(old, new); cases.append(record)
        record = self.record(); record['stderr'] = 'Error: parser failed'; cases.append(record)
        cases.append(self.record('ensures next <= 7 && (next & state) == state'))
        cases.append({'exit_code': 2, 'stdout': 'parse errors detected', 'stderr': ''})
        for record in cases:
            with self.subTest(record=record):
                self.assertEqual(m.classify(record, self.source, self.mutant)[0], 'inconclusive')

    def test_survival_and_empty_verification(self):
        record = {'exit_code': 0, 'stdout': 'Dafny program verifier finished with 41 verified, 0 errors', 'stderr': ''}
        self.assertEqual(m.classify(record, self.source)[0], 'passed')
        self.assertEqual(m.classify(record, self.source, self.mutant)[0], 'survived')
        record['stdout'] = record['stdout'].replace('41 verified', '0 verified')
        self.assertEqual(m.classify(record, self.source)[0], 'inconclusive')

    def test_obligation_timeout_remains_inconclusive_without_process_timeout(self):
        # Actual failure shape from post-merge CI: the process finishes inside
        # its 180-second budget, but an individual Dafny obligation times out.
        record = {'exit_code': 4, 'timed_out': False, 'stdout':
                  "verification/Protocol.dfy(317,20): Error: Verification of 'ProtocolCore.Protocol.HeaderRoundTrip' timed out after 30 seconds.\n"
                  'Dafny program verifier finished with 573 verified, 0 errors, 1 time out\n',
                  'stderr': ''}
        self.assertEqual(m.classify(record, self.source)[0], 'inconclusive')
        self.assertEqual(m.classify(record, self.source, self.mutant)[0], 'inconclusive')

    def test_nonpositive_obligation_limit_fails_before_toolchain_execution(self):
        import json
        for limit in ('0', '-1'):
            with self.subTest(limit=limit), tempfile.TemporaryDirectory() as temporary:
                output = Path(temporary) / 'evidence'
                result = subprocess.run([sys.executable, str(ROOT / 'scripts/run-close-mutations.py'),
                                         '--verification-time-limit', limit, '--output', str(output)],
                                        capture_output=True, text=True, timeout=5)
                self.assertEqual(result.returncode, 1)
                self.assertIn('verification time limit must be positive',
                              json.loads((output / 'failure.json').read_text())['error'])
                self.assertFalse((output / 'toolchain').exists())

    def test_exact_edit_rejects_missing_duplicate_or_contract_edit(self):
        for source in [self.source.replace(self.mutant['before'], ''), self.source + self.mutant['before']]:
            with self.assertRaises(m.v.Invalid):
                m.apply_mutation(source, self.mutant)
        mutant = dict(self.mutant, before=self.mutant['target_clause'], after='ensures true')
        with self.assertRaises(m.v.Invalid):
            m.apply_mutation(self.source, mutant)

    def test_catalog_rejects_dangling_uncovered_and_duplicate_entries(self):
        for change in ('dangling', 'uncovered', 'duplicate', 'target'):
            catalog = copy.deepcopy(self.catalog)
            if change == 'dangling': catalog['mutants'][0]['obligation'] = 'MISSING'
            if change == 'uncovered': catalog['mutants'] = catalog['mutants'][:2]
            if change == 'duplicate': catalog['mutants'].append(catalog['mutants'][0])
            if change == 'target': catalog['mutants'][0]['target_clause'] = 'ensures true'
            from unittest.mock import patch
            original = m.v.read_json
            with patch.object(m.v, 'read_json', side_effect=lambda p: catalog if p.name == 'close.json' else original(p)):
                with self.assertRaises(m.v.Invalid): m.load_catalog()

    def test_suite_baseline_gate_survivor_and_cleanup(self):
        for mode in ('baseline-failure', 'survivor', 'interrupted'):
            trees = []
            def fake(command, tree, directory, budget):
                trees.append(tree)
                self.assertNotEqual(tree, ROOT)
                if len(trees) == 1:
                    self.assertEqual((tree / 'verification/Protocol.dfy').read_text(), self.source)
                else:
                    self.assertNotEqual((tree / 'verification/Protocol.dfy').read_text(), self.source)
                result = {'exit_code': 0, 'stdout': 'Dafny program verifier finished with 41 verified, 0 errors',
                          'stderr': '', 'elapsed_seconds': 0.1}
                if mode == 'baseline-failure': result['exit_code'] = 2
                if mode == 'interrupted' and len(trees) == 2: raise KeyboardInterrupt()
                return result
            with tempfile.TemporaryDirectory() as temporary:
                out = Path(temporary)
                if mode == 'interrupted':
                    with self.assertRaises(KeyboardInterrupt):
                        m.run_suite(self.catalog, self.sources, out, ['unused'], 1, 1, fake)
                else:
                    report = m.run_suite(self.catalog, self.sources, out, ['unused'], 1, 1, fake)
                    self.assertEqual(report['status'], 'failed')
                    self.assertEqual(len(trees), 1 if mode == 'baseline-failure' else 2)
                self.assertTrue((out / 'results.json').is_file())
            self.assertTrue(all(not tree.exists() for tree in trees))
            self.assertEqual((ROOT / 'verification/Protocol.dfy').read_text(), self.source)

    def test_successful_suite_uses_independent_mutants_and_records_evidence(self):
        trees = []
        def fake(command, tree, directory, budget):
            index = len(trees) - 1
            trees.append(tree)
            if len(trees) == 1:
                return {'exit_code': 0, 'stdout': 'Dafny program verifier finished with 41 verified, 0 errors',
                        'stderr': '', 'elapsed_seconds': .1}
            mutant = self.catalog['mutants'][index]
            actual = (tree / 'verification/Protocol.dfy').read_text()
            self.assertEqual(actual, m.apply_mutation(self.source, mutant))
            self.assertEqual((tree / 'verification/Masking.dfy').read_text(), self.sources['verification/Masking.dfy'])
            return self.record(mutant['target_clause'])
        with tempfile.TemporaryDirectory() as temporary:
            report = m.run_suite(self.catalog, self.sources, Path(temporary), ['unused'], 1, 1, fake)
            self.assertEqual(report['status'], 'passed')
            self.assertEqual(len(report['experiments']), 6)
            for experiment in report['experiments'][1:]:
                self.assertIn(experiment['target_clause'], experiment['matched_clauses'])
                self.assertNotEqual(experiment['source_hashes']['verification/Protocol.dfy'],
                                    report['source_hashes']['verification/Protocol.dfy'])
        self.assertEqual(len(set(trees)), 6)
        self.assertTrue(all(not tree.exists() for tree in trees))

    def test_process_timeout_and_missing_executable(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result = m.execute([sys.executable, '-c', 'import time; print("started", flush=True); time.sleep(20)'], root, root / 'timeout', .2)
            self.assertTrue(result['timed_out'])
            self.assertIn('started', result['stdout'])
            self.assertIsNotNone(result['exit_code'])
            result = m.execute([str(root / 'missing')], root, root / 'missing-tool', 1)
            self.assertIn('launch_error', result)

    def test_cli_sigterm_cleans_process_group_and_source_tree(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            marker = root / 'tree'
            # Exercise the same SIGTERM handler, subprocess and TemporaryDirectory
            # lifecycle as the CLI without needing Dafny or spending its budget.
            code = f'''
import importlib.util, pathlib, signal, tempfile
spec=importlib.util.spec_from_file_location('m', {str(ROOT / 'scripts/run-close-mutations.py')!r})
m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
with tempfile.TemporaryDirectory() as tree:
 pathlib.Path({str(marker)!r}).write_text(tree)
 m.execute([{sys.executable!r}, '-c', 'import time; time.sleep(60)'], pathlib.Path(tree), pathlib.Path({str(root / 'logs')!r}), 30)
'''
            child = subprocess.Popen([sys.executable, '-c', code])
            try:
                deadline = time.monotonic() + 5
                while not (root / 'logs/stdout.log').exists() and time.monotonic() < deadline:
                    time.sleep(.01)
                self.assertTrue(marker.exists())
                child.terminate()
                child.wait(timeout=5)
                self.assertFalse(Path(marker.read_text()).exists())
            finally:
                if child.poll() is None:
                    child.kill(); child.wait()


if __name__ == '__main__':
    unittest.main()
