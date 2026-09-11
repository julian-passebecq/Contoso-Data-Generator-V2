"""Real bounded V1.7 dispatch with absent optional metadata; accepts a freshly generated root."""
import argparse
from importlib import metadata
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import uuid

parser = argparse.ArgumentParser(add_help=False)
parser.add_argument('--root', type=Path, required=True)
args, rest = parser.parse_known_args()
root = args.root.resolve()
sys.path.insert(0, str(root / 'factory'))
import run
from common import read


class Dependencies(unittest.TestCase):
    def test_unused_metadata_does_not_fail_real_bronze(self):
        original = metadata.version
        def absent(name):
            if name in ('polars', 'pandas', 'scikit-learn', 'dbt-core'):
                raise metadata.PackageNotFoundError(name)
            return original(name)
        run_id = 'metadata-' + uuid.uuid4().hex
        with patch.object(metadata, 'version', side_effect=absent):
            run.execute(root, run_id, 'verify')
            state = run.execute(root, run_id, 'bronze')
        evidence = read(state / 'run_evidence.json')
        self.assertEqual(evidence['stages']['bronze']['status'], 'succeeded')
        self.assertEqual(evidence['runtimeVersions']['polars'], 'unavailable')
        self.assertEqual(read(state / 'bronze_execution.json')['rowCounts'], read(root / 'truth_manifest.json')['sourceRowCounts'])

    def test_unrelated_metadata_errors_are_not_suppressed(self):
        with patch.object(metadata, 'version', side_effect=RuntimeError('metadata corruption')):
            with self.assertRaisesRegex(RuntimeError, 'metadata corruption'):
                run.available_versions()


if __name__ == '__main__': unittest.main(argv=[sys.argv[0], *rest])
