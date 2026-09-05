"""V1.5 behavior tests, including tamper rejection and independently recomputed held-out metrics."""
import argparse
import copy
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
import pandas as pd
import numpy as np

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "DatabaseGenerator/Forge/Templates/v15"))
from common import compare_kpis, read, write
from ml_lab import temporal_split, algorithms, evaluate, load_features, select_validation_threshold, train_frame, FEATURES, NUMERIC, TARGET
from dbt_runtime import check_results
from run import execute, state_path, verify_artifacts
ROOT = None
STATE = None


class FactoryTests(unittest.TestCase):
    def test_validation_threshold_matches_independent_exhaustive_search_with_ties(self):
        from sklearn.metrics import f1_score, confusion_matrix
        for y, probability in (([0, 1, 0, 1, 1], [.1, .2, .2, .3, .3]), ([0, 1, 0, 1], [.1, .1, .1, .1]),
                               ([0, 0, 1, 1], [0., .5, .5, 1.]), ([0, 1, 1], [1., 1., 1.])):
            probability = np.array(probability)
            result = select_validation_threshold(y, probability)
            candidates = sorted(set(probability) | {0., .5, 1.})
            expected = max(candidates, key=lambda t: (f1_score(y, probability >= t), -abs(t - .5), t))
            self.assertEqual(result["threshold"], expected)
            for row in result["validationTradeoff"]:
                self.assertAlmostEqual(row["f1"], f1_score(y, probability >= row["threshold"]))
                self.assertEqual(row["confusion_matrix"], confusion_matrix(y, probability >= row["threshold"], labels=[0, 1]).tolist())

    def test_threshold_rejects_invalid_validation_data(self):
        for y, p in (([0, 1], [np.nan, .2]), ([0, 1], [-.1, .2]), ([0, 1], [.1, 1.1]), ([1, 1], [.1, .2]), ([0, 1], [.1])):
            with self.assertRaises(ValueError): select_validation_threshold(y, p)

    def test_test_labels_cannot_change_model_or_threshold_selection(self):
        from sklearn.dummy import DummyClassifier
        from sklearn.linear_model import LogisticRegression
        frame = self.frame()
        for column in FEATURES:
            frame[column] = np.arange(len(frame)) % 7 if column in NUMERIC else "category"
        spec = {"features": FEATURES, "leakageExclusions": [TARGET], "featureAvailability": {}, "problemType": "binary_classification"}
        config = {"labelAsOf": "2025-02-01T00:00:00Z", "materializationLimitMb": 16, "seed": 7, "threshold": .5}
        split, _ = temporal_split(frame, config["labelAsOf"])
        with tempfile.TemporaryDirectory() as temporary:
            results = []
            for index in range(2):
                with patch("ml_lab.algorithms", return_value={"dummy": DummyClassifier(), "logistic_regression": LogisticRegression()}):
                    output = Path(temporary) / str(index)
                    train_frame(frame, config, spec, output, {})
                    results.append(read(output / "metrics.json"))
                test = frame.order_key.isin(split.loc[split.split_name == "test", "order_key"])
                frame.loc[test, TARGET] = 1 - frame.loc[test, TARGET]
            self.assertEqual(results[0]["selectedModel"], results[1]["selectedModel"])
            for name in results[0]["models"]:
                self.assertEqual(results[0]["models"][name]["validation"], results[1]["models"][name]["validation"])
                for key in ("threshold", "validationTradeoff", "validation"):
                    self.assertEqual(results[0]["thresholdAnalysis"][name][key], results[1]["thresholdAnalysis"][name][key])

    def test_materialization_guard_runs_before_pandas_load(self):
        from types import SimpleNamespace
        with patch("pyarrow.parquet.read_metadata", return_value=SimpleNamespace(num_rows=100000)), patch("pandas.read_parquet") as materialize:
            with self.assertRaisesRegex(ValueError, "memory budget"):
                load_features(Path("too-large.parquet"), {"materializationLimitMb": 16})
            materialize.assert_not_called()

    def test_modified_report_fails_before_npm(self):
        from build_evidence import build
        with tempfile.TemporaryDirectory() as temporary:
            state = Path(temporary)
            write(state / "bi/report_contract.json", {"status": "package-generated", "reportFileHashes": {"pages/index.md": "0" * 64}})
            with patch("subprocess.run") as npm:
                with self.assertRaisesRegex(ValueError, "artifact changed"): build(state)
                npm.assert_not_called()

    def frame(self):
        times = pd.date_range("2024-01-01", periods=300, tz="UTC")
        return pd.DataFrame({"order_key": range(300), "prediction_time": times, "label_timestamp": times + pd.Timedelta(days=14), TARGET: [i % 3 == 0 for i in range(300)]}).astype({TARGET: int})

    def test_chronological_boundaries_enforce_label_embargo(self):
        frame, partitions = temporal_split(self.frame(), "2025-02-01T00:00:00Z")
        for earlier, later in (("train", "validation"), ("validation", "test")):
            self.assertLess(frame[frame.split_name == earlier].label_timestamp.max(), frame[frame.split_name == later].prediction_time.min())
        self.assertEqual(len(frame.order_key.unique()), len(frame))
        self.assertEqual(set(partitions), {"train", "validation", "test"})

    def test_same_timestamp_never_crosses_partition(self):
        frame = self.frame()
        frame.loc[205:220, "prediction_time"] = frame.loc[210, "prediction_time"]
        frame["label_timestamp"] = frame.prediction_time + pd.Timedelta(days=14)
        split, _ = temporal_split(frame, "2025-02-01T00:00:00Z")
        self.assertTrue((split.groupby("prediction_time").split_name.nunique() <= 1).all())

    def test_immature_labels_never_become_negative_examples(self):
        frame, _ = temporal_split(self.frame(), "2024-09-01T00:00:00Z")
        self.assertTrue((frame.label_timestamp <= pd.Timestamp("2024-09-01T00:00:00Z")).all())

    def test_insufficient_classes_stop_training(self):
        frame = self.frame()
        frame[TARGET] = 0
        with self.assertRaisesRegex(ValueError, "class distribution"): temporal_split(frame, "2025-02-01T00:00:00Z")

    def test_duplicate_grain_and_wrong_label_timestamp_rejected(self):
        frame = self.frame()
        frame.loc[1, "order_key"] = 0
        with self.assertRaisesRegex(ValueError, "one row per order"): temporal_split(frame, "2025-02-01T00:00:00Z")
        frame = self.frame()
        frame.loc[0, "label_timestamp"] += pd.Timedelta(days=1)
        with self.assertRaisesRegex(ValueError, "exactly 14 days"): temporal_split(frame, "2025-02-01T00:00:00Z")

    def test_all_candidate_families_are_bounded_and_seeded(self):
        for problem, required in (("binary_classification", "dummy"), ("regression", "ridge"), ("clustering", "kmeans"), ("anomaly_detection", "isolation_forest")):
            self.assertIn(required, algorithms(problem, 7))
            self.assertLessEqual(len(algorithms(problem, 7)), 5)

    def test_gold_mismatch_null_and_nonfinite_fail(self):
        catalog = {"kpis": [{"id": "orders"}], "reconciliation": {"numericTolerance": 0.000001}}
        truth = {"expectedKpis": {"orders": 12}}
        for bad in (None, 13, float("nan"), float("inf")):
            with self.assertRaises(ValueError): compare_kpis({"orders": bad}, truth, catalog)
        self.assertTrue(compare_kpis({"orders": 12}, truth, catalog)["orders"]["matched"])

    def test_dbt_missing_or_skipped_test_fails_even_with_successful_models(self):
        with tempfile.TemporaryDirectory() as temporary:
            state = Path(temporary)
            write(state / "dbt/target/manifest.json", {"nodes": {"model.a": {"resource_type": "model"}, "test.a": {"resource_type": "test"}}})
            for results in ([{"unique_id": "model.a", "status": "success"}], [{"unique_id": "model.a", "status": "success"}, {"unique_id": "test.a", "status": "skipped"}]):
                write(state / "dbt/target/run_results.json", {"results": results})
                with self.assertRaisesRegex(ValueError, "Incomplete/failed"): check_results(state)

    def test_airflow_run_id_maps_inside_state(self):
        root = Path(tempfile.gettempdir()).resolve()
        state = state_path(root, "manual__2026-09-05T12:00:00+00:00/../../escape")
        self.assertEqual(state.parent, root / ".forge/v15")

    def test_artifact_tampering_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            state = Path(temporary)
            (state / "artifact.txt").write_text("modified")
            with self.assertRaisesRegex(ValueError, "artifact changed"):
                verify_artifacts(state, {"artifacts": {"artifact.txt": "0" * 64}})

    def test_generated_source_tamper_stops_before_transform(self):
        if ROOT is None: self.skipTest("Pass --root to exercise generated runtime identity")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "project"
            shutil.copytree(ROOT, root, ignore=shutil.ignore_patterns(".forge", "__pycache__"))
            (root / "data/source/orders.csv").write_text("tampered")
            with self.assertRaisesRegex(ValueError, "Source checksum mismatch"): execute(root, "tamper", "verify")
            self.assertFalse((root / ".forge/v15/tamper/lake").exists())

    def test_cannot_skip_upstream_stages(self):
        if ROOT is None: self.skipTest("Pass --root to exercise the generated runtime")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "project"
            shutil.copytree(ROOT, root, ignore=shutil.ignore_patterns(".forge", "__pycache__"))
            with self.assertRaisesRegex(ValueError, "requires successful"): execute(root, "skip", "bi")

    def test_measured_metrics_recompute_from_persisted_predictions(self):
        if STATE is None: self.skipTest("Pass --state with an executed ML run")
        metrics = read(STATE / "ml/metrics.json")
        predictions = pd.read_parquet(STATE / "ml/predictions.parquet")
        for (model, split), rows in predictions.groupby(["algorithm", "split_name"]):
            actual = evaluate(rows[TARGET], rows.probability.to_numpy())
            for key in ("average_precision", "roc_auc", "f1", "precision", "recall", "confusion_matrix"):
                self.assertEqual(actual[key], metrics["models"][model][split][key])
            selected = metrics["thresholdAnalysis"][model]
            actual = evaluate(rows[TARGET], rows.probability.to_numpy(), selected["threshold"])
            for key in ("threshold", "average_precision", "roc_auc", "f1", "precision", "recall", "confusion_matrix"):
                self.assertEqual(actual[key], selected[split][key])
            self.assertTrue((rows.prediction_at_selected_threshold == (rows.probability >= selected["threshold"])).all())
            if split == "validation":
                self.assertEqual(select_validation_threshold(rows[TARGET], rows.probability)["threshold"], selected["threshold"])
        expected = max(metrics["models"], key=lambda n: (metrics["models"][n]["validation"]["average_precision"], n))
        self.assertEqual(expected, metrics["selectedModel"])

    def test_report_projects_measured_operating_points_and_pr_curves(self):
        if STATE is None: self.skipTest("Pass --state with an executed ML run")
        import csv
        sources = STATE / "bi/evidence/sources/forge"
        metrics = read(STATE / "ml/metrics.json")
        with (sources / "ml_metrics.csv").open(encoding="utf-8") as stream:
            rows = list(csv.DictReader(stream))
        self.assertEqual(len(rows), 4 * len(metrics["models"]))
        for row in rows:
            model = row["algorithm"]
            measured = (metrics["models"][model] if row["operating_point"] == "baseline 0.5" else metrics["thresholdAnalysis"][model])[row["split"]]
            for key in ("threshold", "f1", "precision", "recall", "average_precision", "roc_auc"):
                self.assertEqual(float(row[key]), measured[key])
        with (sources / "ml_pr_curve.csv").open(encoding="utf-8") as stream:
            for row in csv.DictReader(stream):
                curve = metrics["models"][row["algorithm"]]["validation"]["pr_curve"]
                for key in ("precision", "recall"):
                    self.assertEqual(float(row[key]), curve[key][int(row["point"])])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path)
    parser.add_argument("--state", type=Path)
    args, rest = parser.parse_known_args()
    ROOT, STATE = args.root, args.state
    unittest.main(argv=[sys.argv[0], *rest])
