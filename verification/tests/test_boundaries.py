"""Boundary register failure modes, using the same reference resolver as A."""
import copy
import unittest
import tempfile
from pathlib import Path
from test_requirements import ROOT, v


class BoundaryTests(unittest.TestCase):
    def test_invalid_registers(self):
        manifest = v.read_json(ROOT / 'verification/requirements.json')
        register = v.read_json(ROOT / 'verification/boundaries.json')
        cases = [
            (['schema_version'], 99, 'unsupported value/version'),
            (['boundaries', 1, 'id'], 'CLOSE-LOCK', 'duplicate boundary ID'),
            (['boundaries', 0, 'review_anchor'], 'absent', 'unresolved boundary anchor'),
            (['boundaries', 0, 'assumption'], ' ', 'invalid string'),
            (['boundaries', 0, 'owners'], [], 'too few entries'),
            (['boundaries', 0, 'owners', 0, 'file'], '../escape.cs', 'path outside repository'),
            (['boundaries', 0, 'owners', 0, 'symbol'], 'Demo.Tests.Absent', 'unresolved or ambiguous'),
            (['boundaries', 0, 'requirements'], ['CLOSE-005'], 'dependent requirements'),
            (['boundaries', 0, 'test_obligations'], ['CLOSE-001-PROOF'], 'test obligations'),
            (['boundaries', 0, 'exclusions'], [], 'too few entries'),
            (['boundaries', 0, 'approved'], True, 'unknown fields'),
        ]
        for path, value, error in cases:
            with self.subTest(path=path):
                broken = copy.deepcopy(register)
                parent = broken
                for key in path[:-1]:
                    parent = parent[key]
                parent[path[-1]] = value
                with self.assertRaisesRegex(v.Invalid, error):
                    v.validate(manifest, register=broken)
        broken = copy.deepcopy(register)
        del broken['boundaries'][0]['assumption']
        with self.assertRaisesRegex(v.Invalid, 'missing required fields'):
            v.validate(manifest, register=broken)
        broken = copy.deepcopy(register)
        broken['boundaries'].pop(0)
        with self.assertRaisesRegex(v.Invalid, 'dangling boundary reference'):
            v.validate(manifest, register=broken)

    def test_missing_register_and_duplicate_json_keys_fail(self):
        manifest = v.read_json(ROOT / 'verification/requirements.json')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(v.Invalid, 'missing file'):
                v.validate(manifest, root=root)
            (root / 'verification').mkdir()
            (root / 'verification/boundaries.json').write_text(
                '{"schema_version":1,"schema_version":1}')
            with self.assertRaisesRegex(v.Invalid, 'duplicate JSON key'):
                v.validate(manifest, root=root)
