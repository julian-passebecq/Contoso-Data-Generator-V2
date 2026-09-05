#!/usr/bin/env python3
"""Regression checks for the path-only live-state guard using an isolated Git index."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from check_tracked_runtime_state import forbidden_reason, tracked_violations


class RuntimeStateGuardTests(unittest.TestCase):
    def test_live_directories_and_auth_state_are_rejected(self):
        for path in (".forge-live/project.json", "nested/.Forge-Live/result.json",
                     ".forge-runtime/run.json", ".kube/config", ".config/gcloud/configurations/config_default",
                     "out/test/result_manifest.json", "artifacts/run/work_order.json",
                     "generated/.forge/state/runs/id/result_manifest.json", "generated/.forge/v15/run/ml/metrics.json", "generated/colab/work_order.json",
                     "generated/colab/result_manifest.json", "generated/colab/spark_runtime.json", "generated/colab/work_package.zip",
                     "lake/silver/orders.parquet", "local/application_default_credentials.json",
                     "local/credentials.db", "local/access_tokens.db", ".env", ".env.local", "kubeconfig"):
            with self.subTest(path=path):
                self.assertIsNotNone(forbidden_reason(path))

    def test_authored_examples_templates_and_lake_sentinels_are_allowed(self):
        for path in (".env.example", "examples/free-gcp-lab.project.json", "examples/kubeconfig.example",
                     "DatabaseGenerator/Forge/Templates/free_gcp/colab/work_order.schema.json",
                     "DatabaseGenerator/Forge/Templates/free_gcp/colab/result_manifest.schema.json",
                     "DatabaseGenerator/Forge/Templates/free_gcp/minikube/bootstrap_secrets.py",
                     "references/user_colab/pysparktestj.ipynb", "docs/v131-runtime-evidence.md",
                     "lake/raw/.gitkeep", "lake/.contoso-forge-lake"):
            with self.subTest(path=path):
                self.assertIsNone(forbidden_reason(path))

    def test_index_only_check_catches_deleted_tracked_state_and_ignores_untracked_state(self):
        with tempfile.TemporaryDirectory(prefix="forge-runtime-guard-") as directory:
            root = Path(directory)
            subprocess.run(["git", "init", "--quiet", str(root)], check=True)
            live = root / ".forge-live"
            live.mkdir()
            tracked = live / "project.json"
            tracked.write_text("{}", encoding="utf-8")
            subprocess.run(["git", "-C", str(root), "add", ".forge-live/project.json"], check=True)
            tracked.unlink()
            (live / "untracked.json").write_text("{}", encoding="utf-8")
            self.assertEqual([".forge-live/project.json"], [path for path, _ in tracked_violations(root)])
            subprocess.run(["git", "-C", str(root), "rm", "--cached", "--quiet", ".forge-live/project.json"], check=True)
            self.assertEqual([], tracked_violations(root))

    def test_cli_fails_closed_outside_git(self):
        with tempfile.TemporaryDirectory(prefix="forge-runtime-guard-") as directory:
            result = subprocess.run([sys.executable, str(Path(__file__).with_name("check_tracked_runtime_state.py")),
                                     "--repo", directory], capture_output=True, text=True)
            self.assertEqual(2, result.returncode)
            self.assertIn("Cannot inspect", result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
