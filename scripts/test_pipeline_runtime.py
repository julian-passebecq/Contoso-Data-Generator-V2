#!/usr/bin/env python3
"""Offline tests of a generated neutral plan. All returned warehouse evidence is synthetic test data."""
from __future__ import annotations

import argparse
import contextlib
import copy
from datetime import datetime, timezone
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
PROJECT = None


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PipelineRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="forge-pipeline-runtime-")
        cls.root = Path(cls.temporary.name) / "project"
        shutil.copytree(PROJECT, cls.root)
        config_path = cls.root / "gcp/bigquery_config.json"
        config = json.loads(config_path.read_text(encoding="utf-8-sig"))
        config["gcp"]["projectId"] = "synthetic-offline-project"
        config_path.write_text(json.dumps(config), encoding="utf-8")
        cls.runtime = load_module("pipeline_runtime_under_test", cls.root / "pipeline/forge_pipeline_runtime.py")
        cls.handoff = load_module("pipeline_handoff_under_test", cls.root / "colab/work_order.py")
        cls.plan = json.loads((cls.root / "local_plan.json").read_text(encoding="utf-8-sig"))
        cls.truth = json.loads((cls.root / "truth_manifest.json").read_text(encoding="utf-8-sig"))

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def setUp(self):
        self.run_id = f"manual__{self._testMethodName}:2026-09-04T12:00:00+00:00"
        self.environment = patch.dict(os.environ, {
            "FORGE_PROJECT_ROOT": str(self.root),
            "FORGE_STATE_ROOT": str(Path(self.temporary.name) / "state"),
        })
        self.environment.start()
        self.addCleanup(self.environment.stop)
        actual_run = subprocess.run

        def captured_run(*args, **kwargs):
            kwargs.setdefault("capture_output", True)
            kwargs.setdefault("text", True)
            return actual_run(*args, **kwargs)

        self.capture = patch.object(self.runtime.subprocess, "run", side_effect=captured_run)
        self.capture.start()
        self.addCleanup(self.capture.stop)
        self.output = contextlib.redirect_stdout(io.StringIO())
        self.output.__enter__()
        self.addCleanup(self.output.__exit__, None, None, None)

    def paths(self, run_id=None):
        return self.runtime.run_paths(self.root, run_id or self.run_id, self.plan["pipelineId"])

    def activity(self, operation):
        return next(item for item in self.plan["activities"] if item["operation"] == operation)

    def execute(self, operation):
        return self.runtime.execute_activity(self.activity(operation), root=self.root,
                                             run_id=self.run_id, pipeline_id=self.plan["pipelineId"])

    def fixture_result(self, order):
        """Build a synthetic accepted fixture solely to exercise the checkpoint contract."""
        now = datetime.now(timezone.utc).isoformat()
        prefix = self.handoff.table_prefix(order)
        counts = self.truth["expectedSilverRowCounts"]
        gcp = order["gcp"]
        return {
            "contractVersion": "1.2", "status": "completed", "executionRuntime": "google-colab-interactive",
            **{key: order[key] for key in ("workOrderId", "runId", "datasetFingerprint", "truthManifestSha256")},
            "startedAt": now, "completedAt": now,
            "sourceFileSha256": self.truth["sourceFileSha256"],
            "sourceRowCounts": self.truth["sourceRowCounts"], "silverRowCounts": counts,
            "warehouseRowCounts": counts, "kpis": self.truth["expectedKpis"],
            "warehouse": {"provider": "bigquery", **{key: gcp[key] for key in ("projectId", "dataset", "location")}},
            "loadJobs": {table: {"jobId": "forge_load_" + "a" * 48,
                                "tableId": f"{gcp['projectId']}.{gcp['dataset']}.{prefix}{table}",
                                "state": "DONE", "outputRows": count, "inputSha256": "b" * 64, "sourceFormat": "PARQUET"}
                         for table, count in counts.items()},
            "queryJobs": {**{table: "synthetic_query_" + table for table in counts},
                          "kpis": "synthetic_query_kpis"},
        }

    def test_actual_runner_stops_75_at_missing_human_result_and_preserves_run_identity(self):
        command = [sys.executable, str(self.root / "pipeline/run_local.py"),
                   "--root", str(self.root), "--run-id", self.run_id]
        first = subprocess.run(command, capture_output=True, text=True, timeout=60)
        self.assertEqual(first.returncode, 75, first.stderr)
        self.assertIn("succeeded:verify_source", first.stdout)
        self.assertIn("Return result to:", first.stdout)
        _, order, package, result = self.paths()
        original = json.loads(order.read_text())
        self.assertTrue(package.is_file())
        self.assertFalse(result.exists())
        second = subprocess.run(command, capture_output=True, text=True, timeout=60)
        self.assertEqual(second.returncode, 75, second.stderr)
        self.assertEqual(json.loads(order.read_text()), original)

    def test_sensor_waits_for_absence_and_accepts_only_reconciled_synthetic_fixture(self):
        self.execute("prepare-colab")
        _, order, _, result = self.paths()
        options = dict(root=self.root, run_id=self.run_id, pipeline_id=self.plan["pipelineId"])
        self.assertFalse(self.runtime.sensor_activity(self.activity("await-colab"), **options))
        result.write_text(json.dumps(self.fixture_result(json.loads(order.read_text()))))
        self.assertTrue(self.runtime.sensor_activity(self.activity("await-colab"), **options))
        self.assertTrue(self.execute("reconcile-colab"))

    def test_existing_invalid_result_fails_sensor_instead_of_pending_or_success(self):
        self.execute("prepare-colab")
        _, _, _, result = self.paths()
        result.write_text('{"status":"completed"}')
        with self.assertRaises(subprocess.CalledProcessError):
            self.runtime.sensor_activity(self.activity("await-colab"), root=self.root,
                                         run_id=self.run_id, pipeline_id=self.plan["pipelineId"])

    def test_run_state_is_isolated_and_cross_run_result_is_rejected(self):
        self.execute("prepare-colab")
        _, original_order, _, _ = self.paths()
        old_result = self.fixture_result(json.loads(original_order.read_text()))
        self.run_id += "__next_run"
        self.execute("prepare-colab")
        _, new_order, _, result = self.paths()
        self.assertNotEqual(original_order, new_order)
        result.write_text(json.dumps(old_result))
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute("await-colab")

    def test_unsupported_activity_fails_and_blocks_dependents(self):
        first = copy.deepcopy(self.plan["activities"][0])
        first.update(operation="unsupported", reason="test missing adapter")
        plan = {**self.plan, "activities": [first, *self.plan["activities"][1:]]}
        with self.assertRaises(NotImplementedError):
            self.runtime.run_sequential(plan, self.root, self.run_id)
        self.assertFalse(self.paths()[1].exists())

    def test_source_checksum_tampering_is_detected(self):
        name = next(iter(self.truth["sourceFileSha256"]))
        path = self.root / "data/source" / name
        original = path.read_bytes()
        try:
            path.write_bytes(original + b"\n")
            with self.assertRaisesRegex(RuntimeError, "checksum mismatch"):
                self.execute("verify-source")
        finally:
            path.write_bytes(original)

    def test_sequential_retry_and_subprocess_timeout_are_honored(self):
        activity = copy.deepcopy(self.activity("verify-source"))
        activity.update(maximumAttempts=2, backoffSeconds=0)
        with patch.object(self.runtime, "execute_activity", side_effect=[RuntimeError("transient"), True]) as execute:
            with contextlib.redirect_stdout(io.StringIO()):
                self.runtime.run_sequential({**self.plan, "activities": [activity]}, self.root, self.run_id)
            self.assertEqual(execute.call_count, 2)
        with patch.object(self.runtime.subprocess, "run") as run:
            self.runtime.invoke(self.root, "colab/work_order.py", ["package"], 17)
            self.assertEqual(run.call_args.kwargs["timeout"], 17)
            self.assertTrue(run.call_args.kwargs["check"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    PROJECT = args.project.resolve()
    unittest.main(argv=[sys.argv[0]], verbosity=2)
