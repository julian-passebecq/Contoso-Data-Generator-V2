"""Focused report regressions against freshly generated V1.7 templates."""
import argparse
import csv
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, required=True)
args, rest = parser.parse_known_args()
sys.path.insert(0, str(args.root.resolve() / "factory"))
from common import read, write, sha
import journey_report
import build_evidence


class ReportTests(unittest.TestCase):
    def fixture(self, root, ml=False):
        state = root / "state"
        for name in ("kpi_catalog.json", "semantic_model.json", "lineage.json"):
            write(root / "models" / name, {"kpis": [{"id": "order_count", "name": "Order Count", "format": "whole_number"}]})
        write(state / "reconciliation.json", {"kpis": {} if ml else {"order_count": {"actual": 60, "expected": 60, "matched": True}}})
        for name in ("manifest.json", "run_results.json"):
            write(state / "dbt/target" / name, {})
        evidence = {"goal": "specific-ml" if ml else "kpi-semantic", "runId": "regression", "status": "running",
                    "identity": {"datasetFingerprint": "fixture"}, "stages": {
                        "reconcile": {"status": "succeeded", "result": {"status": "matched"}}, "bi": {"status": "running"}}}
        write(state / "run_evidence.json", evidence)
        if ml:
            write(state / "ml/metrics.json", {"framework": "scikit-learn", "selectedModel": "logistic_regression", "selectedBy": "validation log loss",
                "models": {"logistic_regression": {"test": {"threshold": 0.5, "f1": 0, "precision": 0, "recall": 0, "roc_auc": 0.52, "average_precision": 0.1}}}})
        journey_report.build(root, state, evidence)
        return state, evidence

    def test_snapshot_scope_and_hashes(self):
        with tempfile.TemporaryDirectory() as folder:
            state, original = self.fixture(Path(folder))
            report = state / "bi/evidence"
            snapshot = read(report / "static/contracts/pipeline_evidence.json")
            self.assertEqual(snapshot["status"], "upstream-snapshot")
            self.assertNotIn("bi", snapshot["stages"])
            self.assertEqual(read(state / "run_evidence.json"), original)
            rows = list(csv.DictReader((report / "sources/forge/stages.csv").read_text().splitlines()))
            self.assertEqual([r["stage"] for r in rows], ["reconcile"])
            contract = read(state / "bi/report_contract.json")
            self.assertEqual(contract["inputHashes"]["pipeline_evidence.json"], sha(report / "static/contracts/pipeline_evidence.json"))
            from run import verify_artifacts
            verify_artifacts(report, {"artifacts": contract["reportFileHashes"]})
            (report / "static/contracts/pipeline_evidence.json").write_text("{}")
            with self.assertRaises(ValueError):
                build_evidence.build(state)

    def test_ml_only_keeps_empty_kpi_fix_and_stored_threshold(self):
        with tempfile.TemporaryDirectory() as folder:
            state, _ = self.fixture(Path(folder), ml=True)
            report = state / "bi/evidence"
            self.assertFalse((report / "sources/forge/kpis.csv").exists())
            rows = list(csv.DictReader((report / "sources/forge/ml.csv").read_text().splitlines()))
            self.assertEqual(rows[0]["threshold"], "0.5")
            self.assertIn("stored baseline probability threshold", (report / "pages/index.md").read_text(encoding="utf-8"))

    def test_build_failure_never_claims_success(self):
        with tempfile.TemporaryDirectory() as folder:
            state, _ = self.fixture(Path(folder))
            with patch.object(build_evidence.shutil, "which", return_value="npm"), patch.object(build_evidence.subprocess, "run", side_effect=RuntimeError("build failed")):
                with self.assertRaises(RuntimeError): build_evidence.build(state)
            self.assertEqual(read(state / "bi/build_evidence.json")["status"], "failed")
            self.assertEqual(read(state / "bi/report_contract.json")["renderStatus"], "not-built")

    def test_catalog_formatting(self):
        self.assertEqual(journey_report.display_value(1200, "whole_number"), "1,200")
        self.assertEqual(journey_report.display_value(0.125, "percentage_6dp"), "12.500000%")
        self.assertEqual(journey_report.display_value(1234.5, "currency_2dp"), "1,234.50")
        self.assertEqual(journey_report.display_value("1234.50", "currency_2dp"), "1,234.50")


if __name__ == "__main__": unittest.main(argv=[sys.argv[0], *rest])
