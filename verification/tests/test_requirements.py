"""Run from any directory: python3 -m unittest discover -s verification/tests -v."""
import copy
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('validator', ROOT / 'scripts/validate-requirements.py')
v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)


class RequirementsTests(unittest.TestCase):
    def setUp(self):
        self.manifest = v.read_json(ROOT / 'verification/requirements.json')

    def test_manifest_including_tested_only_requirements(self):
        self.assertEqual(v.validate(self.manifest), 5)
        self.assertEqual(self.manifest['requirements'][3]['contracts'], [])
        self.assertEqual(self.manifest['requirements'][4]['contracts'], [])

    def test_malformed_manifest_fixtures(self):
        fixtures = v.read_json(ROOT / 'verification/tests/invalid-requirements.json')
        for fixture in fixtures:
            with self.subTest(fixture['name']):
                manifest = copy.deepcopy(self.manifest)
                parent = manifest
                for key in fixture['path'][:-1]:
                    parent = parent[key]
                if fixture.get('remove'):
                    del parent[fixture['path'][-1]]
                else:
                    parent[fixture['path'][-1]] = fixture['value']
                with self.assertRaisesRegex(v.Invalid, fixture['error']):
                    v.validate(manifest)

    def test_cli_failure_is_nonzero_and_diagnostic(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'bad.json'
            for content, diagnostic in [('{', 'Expecting'),
                                        ('{"schema_version":1,"schema_version":1}', 'duplicate JSON key')]:
                path.write_text(content)
                run = subprocess.run([sys.executable, str(ROOT / 'scripts/validate-requirements.py'), str(path)],
                                     cwd=directory, capture_output=True, text=True, timeout=10)
                self.assertEqual(run.returncode, 1)
                self.assertIn(diagnostic, run.stderr)
                self.assertNotIn('Validated', run.stdout)

    def test_cli_works_outside_repository(self):
        with tempfile.TemporaryDirectory() as directory:
            run = subprocess.run([sys.executable, str(ROOT / 'scripts/validate-requirements.py')],
                                 cwd=directory, capture_output=True, text=True, timeout=10)
            self.assertEqual(run.returncode, 0, run.stderr)

    def test_source_resolution_rejects_comments_calls_duplicates_and_wrong_scope(self):
        samples = [
            'namespace Demo;\nclass Tests {\n // [Fact]\n // public void Check() {}\n }',
            'namespace Demo;\nclass Tests {\n public void Other() { Check(); }\n }',
            'namespace Demo;\nclass Tests {\n [Fact]\n public void Check() {}\n [Fact]\n public void Check() {}\n }',
            'namespace Demo;\nclass Tests {}\nclass Other {\n [Fact]\n public void Check() {}\n }',
            'namespace Demo;\nclass Tests {\n class Nested {\n [Fact]\n public void Check() {}\n }\n }',
            'namespace Demo;\nclass Tests {\n string s = @"\n public void Check() {}\n";\n }',
        ]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            ref = {'file': 'test.cs', 'symbol': 'Demo.Tests.Check'}
            for sample in samples:
                with self.subTest(sample=sample):
                    (root / 'test.cs').write_text(sample)
                    with self.assertRaises(v.Invalid):
                        v.resolve(root, ref, test=True)
            (root / 'test.cs').write_text('namespace Demo;\nclass Tests {\n [Fact]\n public void Check() {}\n}')
            self.assertIn('Check', v.resolve(root, ref, test=True))

    def test_contract_clause_in_other_method_does_not_resolve(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'test.dfy').write_text('module Demo { class Core {\n static method Check()\n ensures true\n {}\n static method Other()\n ensures false\n {}\n}}')
            header = v.resolve(root, {'file': 'test.dfy', 'symbol': 'Demo.Core.Check'})
            self.assertIn('ensures true', header)
            self.assertNotIn('ensures false', header)

    def test_schema_unknown_keywords_fail_closed(self):
        with self.assertRaisesRegex(v.Invalid, 'unsupported schema keyword'):
            v.shape('anything', {'format': 'uri'})

    def test_missing_review_and_proof_obligations_rejected(self):
        for kind, message in [('review', 'incomplete evidence'), ('proof', 'contract without proof')]:
            with self.subTest(kind=kind):
                manifest = copy.deepcopy(self.manifest)
                req = manifest['requirements'][0]
                req['obligations'] = [o for o in req['obligations'] if o['kind'] != kind]
                with self.assertRaisesRegex(v.Invalid, message):
                    v.validate(manifest)


if __name__ == '__main__':
    unittest.main()
