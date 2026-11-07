"""Real Git histories exercise target selection and changes outside summaries."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('reporter', ROOT / 'scripts/report-contract-changes.py')
r = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r)


class ReportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.git('init', '-b', 'target')
        # Fixture-only identity; never changes repository/user identity.
        self.git('config', 'user.name', 'Test Fixture')
        self.git('config', 'user.email', 'fixture@example.invalid')
        self.put('verification/requirements.json', json.dumps({'requirements':[{'id':'CLOSE-001','intent':'original'}]}))
        self.put('verification/boundaries.json', '{"assumption":"original"}\n')
        self.put('verification/trusted-boundaries.md', 'original external audit\n')
        self.put('verification/Core.dfy', 'function Helper(): bool { true }\nmethod Check()\n requires true\n ensures true\n{}\n')
        self.commit()
        self.base = self.git('rev-parse', 'HEAD').strip()
        self.git('switch', '-c', 'topic')

    def git(self, *args):
        return r.git(self.root, *args)

    def put(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def commit(self):
        self.git('add', '.')
        self.git('commit', '-m', 'chore: fixture')

    def test_reports_intent_contract_helpers_trust_and_full_diff_from_merge_base(self):
        self.put('verification/requirements.json', json.dumps({'requirements':[{'id':'CLOSE-001','intent':'changed intent'}]}))
        self.put('verification/boundaries.json', '{"assumption":"expanded trust"}\n')
        self.put('verification/trusted-boundaries.md', 'changed external audit\n')
        self.put('verification/Core.dfy', 'function Helper(): bool { false }\nmethod Check()\n requires false\n ensures false\n{}\nmethod {:extern} Native()\n ensures true\n')
        self.put('adapter.cs', '// changed caller outside extracted summaries\n')
        self.commit()
        head = self.git('rev-parse', 'HEAD').strip()
        self.git('switch', 'target')
        self.put('target-only.txt', 'must not be reported as a topic deletion\n')
        self.commit()
        self.git('switch', 'topic')
        self.put('adapter.cs', '// uncommitted sentinel\n')
        self.put('core.12592', 'untracked sentinel\n')
        text = r.report(self.root, 'target', 'topic')
        for expected in [self.base, head, 'changed intent', 'expanded trust', 'changed external audit',
                         '+function Helper(): bool { false }', '+ requires false', '+ ensures false',
                         '+method {:extern} Native()', 'changed caller outside extracted summaries',
                         'Semantic review required']:
            self.assertIn(expected, text)
        for excluded in ['target-only.txt', 'uncommitted sentinel', 'untracked sentinel', 'core.12592']:
            self.assertNotIn(excluded, text)

    def test_deleted_and_added_dafny_files_and_unchanged_comparison(self):
        self.assertIn('No committed changes', r.report(self.root, 'target', 'HEAD'))
        (self.root / 'verification/Core.dfy').unlink()
        self.put('new.dfy', 'predicate Replacement() { true }\n')
        self.commit()
        text = r.report(self.root, 'target', 'HEAD')
        self.assertIn('-function Helper()', text)
        self.assertIn('+predicate Replacement()', text)

    def test_missing_target_unrelated_history_and_malformed_manifest_fail(self):
        with self.assertRaises(ValueError):
            r.report(self.root, 'missing', 'HEAD')
        self.put('verification/requirements.json', '{')
        self.commit()
        with self.assertRaises(ValueError):
            r.report(self.root, 'target', 'HEAD')
        self.git('checkout', '--orphan', 'unrelated')
        self.git('commit', '-m', 'chore: independent fixture')
        with self.assertRaises(ValueError):
            r.report(self.root, 'target', 'HEAD')

    def test_markdown_fence_preserves_embedded_fences(self):
        self.assertTrue(r.fence('```\nsource\n```').startswith('````diff'))

    def test_cli_requires_explicit_target(self):
        run = subprocess.run(['python3', str(ROOT / 'scripts/report-contract-changes.py'),
                              '--output', str(self.root / 'report.md')], capture_output=True, text=True)
        self.assertNotEqual(run.returncode, 0)
        self.assertIn('--target', run.stderr)
        self.assertFalse((self.root / 'report.md').exists())
