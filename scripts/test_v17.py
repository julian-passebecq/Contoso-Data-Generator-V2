"""V1.7 measured gates and adversarial adapter contracts. Mock receipts are never release execution proof."""
import argparse
import copy
import importlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

PARSER = argparse.ArgumentParser(add_help=False)
PARSER.add_argument("--gate", type=Path, required=True)
ARGS, REST = PARSER.parse_known_args()
GATE = ARGS.gate.resolve()
SUMMARY = json.loads((GATE / "gate.json").read_text())
ROOT = Path(SUMMARY["runs"]["duckdb"]["root"])
STATE = Path(SUMMARY["runs"]["duckdb"]["state"])
sys.path.insert(0, str(ROOT / "factory"))
from common import read, write, sha
from external_contract import hashes, verify, binding, validate_results
import external_labs


class GateTests(unittest.TestCase):
    def test_three_engine_logical_parity(self):
        evidence = read(GATE / "engine_parity.json")
        self.assertEqual(evidence["status"], "matched")
        self.assertEqual(len(evidence["tables"]), 13)
        self.assertTrue({"duckdb", "polars", "pandas"} <= set(evidence["runs"]))

    def test_stops_exclude_downstream_files_and_operations(self):
        orders = ["generate", "bronze", "silver", "gold", "semantic", "analysis", "report", "publish"]
        for stop in orders[:6]:
            with self.subTest(stop=stop):
                row = SUMMARY["runs"]["stop-" + stop]; root, state = Path(row["root"]), Path(row["state"])
                evidence = read(state / "run_evidence.json")
                self.assertEqual(evidence["status"], "succeeded")
                self.assertEqual(evidence["stopAfter"], stop)
                for stage, path in (("bronze", "lake/bronze"), ("silver", "lake/silver"), ("gold", "dbt"), ("semantic", "semantic_execution.json"), ("report", "bi"), ("analysis", "ml")):
                    if orders.index(stage) > orders.index(stop): self.assertFalse((state / path).exists(), path)
                from run import execute
                if stop != "report":
                    with self.assertRaisesRegex(ValueError, "boundary"): execute(root, "v17", "bi")

    def test_bronze_has_own_truth_count_and_file_evidence(self):
        for engine in ("duckdb", "polars", "pandas"):
            row = SUMMARY["runs"][engine]; root, state = Path(row["root"]), Path(row["state"])
            from layers import verify_bronze
            bronze = verify_bronze(root, state)
            self.assertEqual(bronze["rowCounts"], read(root / "truth_manifest.json")["sourceRowCounts"])
            self.assertEqual(read(state / "run_evidence.json")["stages"]["silver"]["result"]["input"], "persisted-bronze")

    def test_all_five_kpis_and_queries_reconcile(self):
        import duckdb
        for engine in ("duckdb", "polars", "pandas"):
            row = SUMMARY["runs"][engine]; root, state = Path(row["root"]), Path(row["state"])
            result = read(state / "semantic_execution.json")
            self.assertEqual(len(result["kpis"]), 5)
            self.assertTrue(all(v["matched"] for v in result["kpis"].values()))
            with duckdb.connect(str(state / "warehouse.duckdb"), read_only=True) as db:
                for query in (root / "models/query_examples.sql").read_text().splitlines(): self.assertEqual(len(db.execute(query).fetchall()), 1)

    def test_scoped_report_omits_unselected_kpis(self):
        row = SUMMARY["runs"]["stop-semantic"]
        root, state = Path(row["root"]), Path(row["state"])
        self.assertEqual([k["id"] for k in read(root / "models/kpi_catalog.json")["kpis"]], ["return_rate"])
        self.assertEqual(list(read(state / "semantic_execution.json")["kpis"]), ["return_rate"])
        self.assertFalse(read(root / "models/lineage.json")["physicalPruning"])

    def test_no_auth_states_do_not_claim_external_execution(self):
        kaggle = Path(SUMMARY["runs"]["automl-kaggle"]["state"])
        hf = Path(SUMMARY["runs"]["specific-ml"]["state"])
        md = Path(SUMMARY["runs"]["motherduck"]["state"])
        self.assertEqual(read(kaggle / "kaggle_execution.json")["status"], "exported-not-executed")
        self.assertEqual(read(hf / "huggingface_execution.json")["status"], "exported-not-published")
        self.assertEqual(read(md / "motherduck_execution.json")["status"], "exported-not-executed")
        self.assertFalse((md / "dbt/target/run_results.json").exists())
        self.assertFalse((md / "bi").exists())

    def test_specific_algorithm_is_selected_before_test(self):
        state = Path(SUMMARY["runs"]["specific-ml"]["state"])
        metrics = read(state / "ml/metrics.json")
        self.assertEqual(set(metrics["models"]), {"logistic_regression"})
        self.assertEqual(metrics["thresholdAnalysis"]["logistic_regression"]["selectedOn"], "validation")
        self.assertEqual(read(state / "huggingface/model/model_card.json")["identity"], metrics["identity"])

    def test_static_hf_space_self_contained(self):
        state = Path(SUMMARY["runs"]["specific-ml"]["state"])
        folder = state / "huggingface"
        verify(folder, read(folder / "package_manifest.json")["files"], ("package_manifest.json",))
        self.assertIn("sdk: static", (folder / "space/README.md").read_text())
        self.assertNotIn("<script src=", (folder / "space/index.html").read_text())
        self.assertFalse(any(p.suffix in (".pkl", ".pickle", ".joblib") for p in folder.rglob("*")))

    def test_ml_only_report_omits_empty_kpi_source(self):
        state = Path(SUMMARY["runs"]["specific-ml"]["state"])
        self.assertFalse((state / "bi/evidence/sources/forge/kpis.csv").exists())
        self.assertIn("Origin: **scikit-learn**", (state / "bi/evidence/pages/index.md").read_text())

    def test_colab_spark_export_preserved(self):
        state = Path(SUMMARY["runs"]["spark-ml"]["state"])
        code = (state / "exports/colab-spark-ml.ipynb").read_text()
        self.assertIn("spark_ml.py", code)
        self.assertIn("4.0.4", code)
        self.assertFalse((state / "ml/metrics.json").exists())


class IsolatedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="forge-v17-test-")
        self.state = Path(self.temp.name) / "state"
        self.state.mkdir()

    def tearDown(self): self.temp.cleanup()

    def copy_state(self, source=STATE):
        for relative in ("run_evidence.json", "dbt_execution.json", "lake/gold/ml_customer_dissatisfaction.parquet"):
            target = self.state / relative; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source / relative, target)

    def config(self):
        return {"kind": "mljar", "runtime": "kaggle", "target": "is_dissatisfied_14d", "mode": "Explain", "totalTimeLimitSeconds": 30,
                "timeoutSeconds": 120, "execute": True, "requireExecution": False, "datasetId": "test-owner/test-dataset", "kernelId": "test-owner/test-kernel", "versionDataset": False}

    def make_package(self):
        self.copy_state()
        self.configured = self.config()
        return external_labs.package(ROOT, self.state, self.configured)

    def results(self, output, bound, remote):
        import pandas as pd
        from ml_lab import evaluate
        output.mkdir(parents=True)
        rows, models = [], {}
        for split, expected in bound["expectedPredictions"].items():
            batch = [{"order_key": int(key), "split_name": split, "is_dissatisfied_14d": label, "probability": .8 if label else .2} for key, label in expected.items()]
            frame = pd.DataFrame(batch)
            models[split] = evaluate(frame.is_dissatisfied_14d.to_numpy(), frame.probability.to_numpy())
            rows.extend(batch)
        pd.DataFrame(rows).to_parquet(output / "predictions.parquet", index=False)
        metrics = {"status": "executed", "framework": "mljar-supervised", "identity": bound["identity"], "selectedModel": "fixture",
            "selectedBy": "fixture only", "models": {"fixture": models}, "partitions": bound["partitions"]}
        write(output / "metrics.json", metrics)
        write(output / "selected_model.json", {"name": "fixture", "identity": bound["identity"], "selectedBeforeTest": True})
        write(output / "automl_execution.json", {"status": "executed", "origin": "test-fixture"})
        write(output / "report/summary.json", {"testFixture": True})
        (output / "leaderboard.csv").write_text("name,metric_value\nfixture,0.1\n")
        self.seal(output, bound, remote)

    def seal(self, output, bound, remote):
        write(output / "remote_manifest.json", {"status": "executed", "identity": bound["identity"], "remote": remote,
            "packageManifestSha256": bound["manifestSha256"], "target": bound["target"], "splitContract": bound["splitContract"], "files": hashes(output, ("remote_manifest.json",))})


class IntegrityTests(IsolatedTests):
    def test_bronze_mutation_fails_closed(self):
        for name in ("bronze_execution.json", "run_evidence.json"):
            shutil.copyfile(STATE / name, self.state / name)
        shutil.copytree(STATE / "lake/bronze", self.state / "lake/bronze")
        from layers import verify_bronze
        verify_bronze(ROOT, self.state)
        path = next((self.state / "lake/bronze").rglob("*.parquet"))
        path.write_bytes(path.read_bytes() + b"tamper")
        with self.assertRaisesRegex(ValueError, "checksum"): verify_bronze(ROOT, self.state)

    def test_extra_bronze_file_fails_closed(self):
        for name in ("bronze_execution.json", "run_evidence.json"): shutil.copyfile(STATE / name, self.state / name)
        shutil.copytree(STATE / "lake/bronze", self.state / "lake/bronze")
        (self.state / "lake/bronze/extra.parquet").write_bytes(b"extra")
        from layers import verify_bronze
        with self.assertRaises(ValueError): verify_bronze(ROOT, self.state)

    def test_kaggle_package_is_private_and_bound(self):
        bound = self.make_package()
        folder = self.state / "kaggle"
        verify(folder / "dataset", bound["files"], ("package_manifest.json",))
        self.assertTrue(read(folder / "dataset/dataset-metadata.json")["isPrivate"])
        self.assertTrue(read(folder / "notebook/kernel-metadata.json")["is_private"])
        self.assertFalse(read(folder / "notebook/kernel-metadata.json")["enable_gpu"])
        self.assertIn(bound["manifestSha256"], (folder / "notebook/experiment.ipynb").read_text())

    def test_changed_dataset_rejected_before_submission(self):
        self.make_package()
        (self.state / "kaggle/dataset/spec.json").write_text("{}")
        with self.assertRaises(ValueError): external_labs.execute_kaggle(ROOT, self.state, self.configured, call=lambda *a, **k: self.fail("No network expected"))

    def test_kaggle_missing_auth_exports_cleanly(self):
        self.make_package()
        with patch.object(external_labs, "authenticated", return_value=False):
            result = external_labs.execute_kaggle(ROOT, self.state, self.configured)
        self.assertEqual(result["status"], "exported-not-executed")

    def test_required_kaggle_missing_auth_fails(self):
        self.make_package(); self.configured["requireExecution"] = True
        with patch.object(external_labs, "authenticated", return_value=False), self.assertRaises(ValueError): external_labs.execute_kaggle(ROOT, self.state, self.configured)

    def test_wrong_run_fingerprint_and_output_hash_rejected(self):
        bound = self.make_package(); remote = {"datasetId": "test-owner/test-dataset", "kernelId": "test-owner/test-kernel", "datasetVersion": 3}
        output = self.state / "fixture"; self.results(output, bound, remote)
        validate_results(output, bound, remote)
        original = read(output / "remote_manifest.json")
        for key in ("runId", "datasetFingerprint", "featureSha256"):
            with self.subTest(key=key):
                modified = copy.deepcopy(original); modified["identity"][key] = "stale"; write(output / "remote_manifest.json", modified)
                with self.assertRaises(ValueError): validate_results(output, bound, remote)
        write(output / "remote_manifest.json", original)
        (output / "leaderboard.csv").write_text("changed")
        with self.assertRaises(ValueError): validate_results(output, bound, remote)

    def test_wrong_partition_prediction_or_metric_rejected_even_with_new_hash(self):
        bound = self.make_package(); remote = {"datasetId": "test-owner/test-dataset", "kernelId": "test-owner/test-kernel", "datasetVersion": 3}
        for mutation in ("partition", "metric", "order", "label"):
            with self.subTest(mutation=mutation):
                output = self.state / mutation; self.results(output, bound, remote)
                metrics = read(output / "metrics.json")
                if mutation == "partition": metrics["partitions"]["test"]["rows"] += 1
                elif mutation == "metric": metrics["models"]["fixture"]["test"]["f1"] = 0
                else:
                    import pandas as pd
                    p = pd.read_parquet(output / "predictions.parquet")
                    p.loc[0, "order_key" if mutation == "order" else "is_dissatisfied_14d"] = -1
                    p.to_parquet(output / "predictions.parquet", index=False)
                write(output / "metrics.json", metrics); self.seal(output, bound, remote)
                with self.assertRaises(ValueError): validate_results(output, bound, remote)

    def test_successful_mock_remote_download_is_validated_before_ingestion(self):
        bound = self.make_package(); calls = []
        remote = {"datasetId": "test-owner/test-dataset", "kernelId": "test-owner/test-kernel", "datasetVersion": 3}
        def call(args, **kwargs):
            calls.append(args)
            if args[:2] == ["datasets", "status"]: return json.dumps({"status": "ready", "current_version_number": 3})
            if args[:2] == ["kernels", "status"]: return 'test-owner/test-kernel has status "complete"'
            if args[:2] == ["kernels", "output"]: self.results(Path(args[-1]) / "results", bound, remote)
            return ""
        with patch.object(external_labs, "authenticated", return_value=True): result = external_labs.execute_kaggle(ROOT, self.state, self.configured, call=call)
        self.assertEqual(result["status"], "executed")
        self.assertTrue(result["remoteManifestValidated"])
        self.assertEqual(result["remote"]["datasetVersion"], 3)
        self.assertTrue((self.state / "ml/metrics.json").exists())
        self.assertFalse(any("--public" in c for c in calls))

    def test_timeout_never_downloads_or_succeeds(self):
        self.make_package(); ticks = [0]
        def call(args, **kwargs):
            self.assertNotEqual(args[:2], ["kernels", "output"])
            return json.dumps({"status": "creating", "current_version_number": 1}) if args[:2] == ["datasets", "status"] else ""
        def sleep(seconds): ticks[0] += seconds
        with patch.object(external_labs, "authenticated", return_value=True), self.assertRaises(RuntimeError):
            external_labs.execute_kaggle(ROOT, self.state, self.configured, call=call, clock=lambda: ticks[0], sleep=sleep)
        self.assertEqual(read(self.state / "kaggle_execution.json")["status"], "failed")
        self.assertFalse((self.state / "ml").exists())

    def test_path_injection_rejected(self):
        with self.assertRaises(ValueError): verify(self.state, {"../outside": "a" * 64})


class AutoMlTests(IsolatedTests):
    def test_custom_validation_never_passes_test_to_fit_and_freezes_selection(self):
        bound = self.make_package()
        from automl_worker import train
        import numpy as np
        import pandas as pd
        observed = {}
        class AutoML:
            def __init__(self, **kwargs):
                observed.update(kwargs)
                self.folder = Path(kwargs["results_path"]); self.folder.mkdir(parents=True)
            def fit(self, X, y, cv):
                from ml_lab import NUMERIC
                if any(X[column].dtype != np.dtype("float64") for column in NUMERIC): raise AssertionError("Numeric features became text")
                self.assertions = (X, y, cv)
                observed["fitRows"] = len(X); observed["cv"] = cv
                write(self.folder / "params.json", {"best_model": "fixture"})
                (self.folder / "loser").mkdir()
                (self.folder / "loser/importance.csv").write_text("feature,importance\nsales_amount,1\n")
            def get_leaderboard(self, **kwargs): return pd.DataFrame([{"name": "fixture", "metric_value": .3}])
            def predict_proba(self, X):
                if len(X) == bound["partitions"]["test"]["rows"]:
                    if not (self.folder.parent / "selected_model.json").exists(): raise AssertionError("Test was evaluated before selection froze")
                p = np.linspace(.15, .85, len(X)); return np.c_[1-p, p]
        output = self.state / "automl"
        train(self.state / "kaggle/dataset", output, bound["identity"], AutoML)
        self.assertEqual(observed["validation_strategy"], {"validation_type": "custom"})
        self.assertEqual(observed["total_time_limit"], 30)
        self.assertEqual(observed["fitRows"], sum(bound["partitions"][k]["rows"] for k in ("train", "validation")))
        self.assertEqual(set(observed["cv"][0][0]) & set(observed["cv"][0][1]), set())
        self.assertEqual(read(output / "automl_execution.json")["testRowsPassedToFit"], 0)
        self.assertFalse((output / "feature_importance.csv").exists())

    def test_notebook_process_hard_timeout_does_not_emit_success_manifest(self):
        self.make_package()
        from notebook_runner import run
        with patch("notebook_runner.subprocess.run", side_effect=subprocess.TimeoutExpired("worker", 90)), self.assertRaises(subprocess.TimeoutExpired):
            run(self.state / "kaggle/dataset", self.state / "timed-out", {})
        self.assertFalse((self.state / "timed-out/remote_manifest.json").exists())
        self.assertEqual(read(self.state / "timed-out/failure.json")["status"], "failed")


class HuggingFaceTests(IsolatedTests):
    def setup_hf(self):
        source = Path(SUMMARY["runs"]["specific-ml"]["state"])
        self.copy_state(source)
        shutil.copytree(source / "huggingface", self.state / "huggingface")
        shutil.copytree(source / "ml", self.state / "ml")
        self.root = Path(SUMMARY["runs"]["specific-ml"]["root"])
        return {"execute": True, "requireExecution": False, "public": False, "modelRepoId": "test-owner/test-model", "spaceRepoId": "test-owner/test-space"}

    def test_publish_records_actual_returned_revisions_and_static_sdk(self):
        from huggingface_export import publish
        target = self.setup_hf(); calls = []
        class Api:
            def create_repo(self, **kwargs): calls.append(kwargs)
            def repo_info(self, **kwargs): return SimpleNamespace(private=True)
            def upload_folder(self, **kwargs): return SimpleNamespace(oid="a" * 40)
        with patch.dict(os.environ, {"HF_TOKEN": "test-token-never-persist"}): result = publish(self.root, self.state, target, Api())
        self.assertEqual(result["status"], "published")
        self.assertEqual(len(result["repositories"]), 2)
        self.assertEqual(calls[1]["space_sdk"], "static")
        self.assertTrue(all(c["private"] for c in calls))
        self.assertNotIn("test-token-never-persist", json.dumps(result))

    def test_stale_package_prevents_any_publication(self):
        from huggingface_export import publish
        target = self.setup_hf()
        package = read(self.state / "huggingface/package_manifest.json"); package["identity"]["runId"] = "stale"; write(self.state / "huggingface/package_manifest.json", package)
        with self.assertRaises(ValueError): publish(self.root, self.state, target, object())

    def test_public_repo_does_not_receive_private_intent_package(self):
        from huggingface_export import publish
        target = self.setup_hf()
        class Api:
            def create_repo(self, **kwargs): pass
            def repo_info(self, **kwargs): return SimpleNamespace(private=False)
            def upload_folder(self, **kwargs): raise AssertionError("Must reject before upload")
        with self.assertRaises(RuntimeError): publish(self.root, self.state, target, Api())
        self.assertEqual(read(self.state / "huggingface_execution.json")["status"], "failed")

    def test_missing_revision_cannot_be_published(self):
        from huggingface_export import publish
        target = self.setup_hf()
        class Api:
            def create_repo(self, **kwargs): pass
            def repo_info(self, **kwargs): return SimpleNamespace(private=True)
            def upload_folder(self, **kwargs): return SimpleNamespace(oid=None)
        with self.assertRaises(RuntimeError): publish(self.root, self.state, target, Api())


class WranglingTests(IsolatedTests):
    def recipe(self):
        return {"recipeVersion": 1, "maxRows": 10000, "steps": [
            {"id": "orders", "op": "source", "entity": "orders"},
            {"id": "pick", "op": "select", "input": "orders", "columns": ["OrderKey", "StoreKey"]},
            {"id": "rename", "op": "rename", "input": "pick", "renames": {"StoreKey": "store"}},
            {"id": "cast", "op": "cast", "input": "rename", "casts": {"OrderKey": "int64"}},
            {"id": "derive", "op": "derive", "input": "cast", "name": "doubled", "expression": {"operator": "*", "args": [{"field": "OrderKey"}, {"value": 2}]}},
            {"id": "filtered", "op": "filter", "input": "derive", "predicate": {"operator": ">", "args": [{"field": "doubled"}, {"value": 10}]}},
            {"id": "split", "op": "conditional-split", "input": "filtered", "matched": False, "predicate": {"operator": ">", "args": [{"field": "OrderKey"}, {"value": 100}]}},
            {"id": "dedup", "op": "deduplicate", "input": "split", "columns": ["OrderKey"]},
            {"id": "window", "op": "window", "input": "dedup", "name": "position", "function": "row_number", "groupBy": ["store"], "orderBy": ["OrderKey"]},
            {"id": "daily", "op": "aggregate", "input": "window", "groupBy": ["store"], "measures": [{"name": "orders", "function": "count"}, {"name": "amount", "function": "mean", "field": "doubled"}]},
            {"id": "lookup_source", "op": "source", "entity": "stores"},
            {"id": "lookup_pick", "op": "select", "input": "lookup_source", "columns": ["StoreKey", "StoreName"]},
            {"id": "lookup_rename", "op": "rename", "input": "lookup_pick", "renames": {"StoreKey": "store"}},
            {"id": "joined", "op": "lookup", "input": "daily", "right": "lookup_rename", "on": ["store"], "how": "left"},
            {"id": "output", "op": "sink", "input": "joined"}]}

    def setup_recipe(self):
        shutil.copytree(STATE / "lake/silver", self.state / "lake/silver")
        shutil.copyfile(STATE / "silver_contract.json", self.state / "silver_contract.json")

    def test_same_recipe_matches_across_duckdb_and_polars(self):
        self.setup_recipe()
        from wrangling import execute
        import pyarrow.parquet as pq
        from parity import compare_tables
        outputs = {}
        for engine in ("duckdb", "polars"):
            state = self.state / engine
            shutil.copytree(self.state / "lake", state / "lake")
            shutil.copyfile(self.state / "silver_contract.json", state / "silver_contract.json")
            result = execute(ROOT, state, self.recipe(), engine)
            self.assertEqual(result["status"], "executed")
            outputs[engine] = pq.read_table(state / "wrangling/output.parquet")
        governed = {"key": ["store"], "unique": True, "columns": [
            {"name": "store", "type": "int32", "nullable": False}, {"name": "orders", "type": "int64", "nullable": False},
            {"name": "amount", "type": "float64", "nullable": True, "decimalPlaces": 9}, {"name": "StoreName", "type": "string", "nullable": True}]}
        comparison = compare_tables(outputs, governed)
        self.assertTrue(comparison["matched"], comparison)

    def test_recipe_cycle_and_unknown_operator_fail(self):
        from wrangling import validate
        for steps in ([{"id": "x", "op": "sink", "input": "x"}], [{"id": "x", "op": "shell", "input": "orders"}]):
            with self.assertRaises(ValueError): validate({"recipeVersion": 1, "steps": steps})

    def test_unknown_fields_code_and_path_injection_rejected(self):
        from wrangling import validate, expression
        for value in ("../orders", "orders;DROP TABLE x", "$(whoami)"):
            with self.assertRaises(ValueError): validate({"recipeVersion": 1, "steps": [{"id": value, "op": "source", "entity": "orders"}]})
        with self.assertRaises(ValueError): expression({"operator": "eval", "args": [{"value": "__import__('os')"}]}, [], "duckdb")
        with self.assertRaises(ValueError): expression({"field": "absent"}, ["OrderKey"], "polars")

    def test_materialization_limit_is_enforced(self):
        self.setup_recipe()
        from wrangling import execute
        recipe = self.recipe(); recipe["maxRows"] = 1
        with self.assertRaisesRegex(ValueError, "maxRows"): execute(ROOT, self.state, recipe, "duckdb")


class MotherDuckTests(IsolatedTests):
    def prepare_silver(self):
        for relative in ("bronze_execution.json", "run_evidence.json"):
            shutil.copyfile(STATE / relative, self.state / relative)
        shutil.copytree(STATE / "lake", self.state / "lake")
        for name in ("silver_counts.json", "silver_contract.json", "bronze_silver.duckdb"):
            if (STATE / name).exists(): shutil.copyfile(STATE / name, self.state / name)
        self.target = {"database": "forge_test_fresh", "execute": True, "requireExecution": False}

    def test_missing_token_never_runs_dbt_or_remote_connection(self):
        self.prepare_silver()
        from motherduck import publish_silver_and_build
        with patch.dict(os.environ, {}, clear=True), patch("duckdb.connect", side_effect=AssertionError("No remote connection expected")):
            result = publish_silver_and_build(ROOT, self.state, self.target)
        self.assertEqual(result["status"], "exported-not-executed")
        self.assertFalse((self.state / "dbt").exists())

    def test_reconciliation_uses_remote_database_and_summary_is_bound(self):
        from journey_runtime import dispatch
        from run import verify_motherduck_summary
        write(self.state / "motherduck_build.json", {"database": "forge_fresh", "status": "dbt-executed-awaiting-reconciliation"})
        with patch("dbt_runtime.reconcile", return_value={"status": "reconciled", "kpis": {}}) as reconcile:
            dispatch(ROOT, self.state, {}, {"warehouse": "motherduck"}, "reconcile", {})
        reconcile.assert_called_once_with(ROOT, self.state, database_path="md:forge_fresh")
        verify_motherduck_summary(self.state)
        write(self.state / "motherduck_execution.json", {"status": "executed", "database": "wrong"})
        with self.assertRaises(ValueError): verify_motherduck_summary(self.state)

    def test_no_auth_dive_export_reads_only_governed_gold(self):
        from journey_runtime import dispatch
        write(self.state / "motherduck_build.json", {"database": "forge_fresh", "status": "exported-not-executed"})
        target = {"kind": "motherduck", "database": "forge_fresh", "createDive": True, "execute": True}
        with patch.dict(os.environ, {}, clear=True), patch("duckdb.connect", side_effect=AssertionError("No remote connection")):
            result = dispatch(ROOT, self.state, {"publishTargets": [target]}, {}, "publish", {})
        self.assertEqual(result["status"], "exported-not-executed")
        self.assertEqual(result["dive"]["status"], "exported-not-published")
        self.assertNotIn("url", result["dive"])
        query = read(self.state / "dive_query.json")["query"]
        self.assertIn('"forge_fresh".gold.kpi_customer_satisfaction', query)
        self.assertNotIn("silver", query)

    def test_native_destination_counts_dbt_and_invocation_are_bound(self):
        self.prepare_silver()
        from motherduck import publish_silver_and_build
        counts = read(ROOT / "truth_manifest.json")["expectedSilverRowCounts"]
        commands, builds = [], []
        class Db:
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def execute(self, sql, parameters=None): commands.append(sql); self.sql = sql; return self
            def fetchone(self): return [next(count for name, count in counts.items() if self.sql.endswith('"' + name + '"'))]
        def build(root, state, database_path=None):
            builds.append(database_path)
            for name in ("manifest.json", "run_results.json"): write(state / "dbt/target" / name, {"metadata": {"invocation_id": "mock-invocation"}})
            return {"status": "tested"}
        with patch.dict(os.environ, {"MOTHERDUCK_TOKEN": "test-secret"}), patch("duckdb.connect", return_value=Db()), patch("dbt_runtime.build", side_effect=build):
            result = publish_silver_and_build(ROOT, self.state, self.target)
        self.assertEqual(builds, ["md:forge_test_fresh"])
        self.assertEqual(result["silverCounts"], counts)
        self.assertEqual(result["dbtInvocationId"], "mock-invocation")
        self.assertIn('CREATE DATABASE "forge_test_fresh"', commands)
        self.assertFalse(any("IF NOT EXISTS" in c for c in commands))
        self.assertNotIn("test-secret", json.dumps(result))

    def test_wrong_native_counts_fail_before_dbt(self):
        self.prepare_silver()
        from motherduck import publish_silver_and_build
        class Db:
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def execute(self, *args): return self
            def fetchone(self): return [-1]
        with patch.dict(os.environ, {"MOTHERDUCK_TOKEN": "test-secret"}), patch("duckdb.connect", return_value=Db()), patch("dbt_runtime.build", side_effect=AssertionError("dbt must not start")), self.assertRaises(RuntimeError):
            publish_silver_and_build(ROOT, self.state, self.target)
        self.assertEqual(read(self.state / "motherduck_execution.json")["status"], "failed")


if __name__ == "__main__": unittest.main(argv=[sys.argv[0], *REST])
