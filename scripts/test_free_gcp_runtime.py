#!/usr/bin/env python3
"""Offline contract tests. Fake BigQuery jobs do not constitute live cloud validation."""
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
import shutil
import sys
import tempfile
import types
import unittest
import zipfile
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from pathlib import Path
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[1]
TEMPLATES = REPO / "DatabaseGenerator/Forge/Templates/free_gcp"
sys.dont_write_bytecode = True
sys.path.insert(0, str(TEMPLATES / "colab"))
sys.path.insert(0, str(TEMPLATES / "gcp"))
import work_order as handoff
import bigquery_runtime as runtime

CONFIG = {"warehouse": "bigquery", "maximumLocalFileBytes": 100_000_000,
          "gcp": {"projectId": "example-project", "dataset": "contoso_forge", "location": "US", "maximumBytesBilled": 1_000_000}}


class ApiError(Exception):
    def __init__(self, code):
        self.code = code


class FakeConfig:
    pass


API = types.SimpleNamespace(LoadJobConfig=FakeConfig, QueryJobConfig=FakeConfig,
                            SchemaField=types.SimpleNamespace(from_api_repr=lambda value: value))


class FakeJob:
    def __init__(self, job_id, rows=None, output_rows=1):
        self.job_id, self.rows, self.output_rows = job_id, rows or [], output_rows
        self.state, self.errors, self.waited = "DONE", None, 0

    def result(self, timeout=None):
        self.waited += 1
        return self.rows


class FakeClient:
    def __init__(self):
        self.jobs, self.submissions, self.queries = {}, [], []
        self.query_count = 1
        self.query_kpi = "1"
        self.ambiguous_submit = False

    def get_job(self, job_id, **kwargs):
        if job_id not in self.jobs:
            raise ApiError(404)
        return self.jobs[job_id]

    def load_table_from_file(self, source, table, **kwargs):
        self.submissions.append((source.read(), table, kwargs))
        job = FakeJob(kwargs["job_id"])
        self.jobs[job.job_id] = job
        if self.ambiguous_submit:
            raise ApiError(503)
        return job

    def query(self, sql, **kwargs):
        self.queries.append((sql, kwargs))
        rows = [{"row_count": self.query_count}] if "AS row_count" in sql else [{"order_count": self.query_kpi}]
        return FakeJob("query_" + str(len(self.queries)), rows)


class WorkOrderTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="forge-runtime-tests-")
        self.root = Path(self.temp.name)
        for directory in ("gcp", "colab"):
            shutil.copytree(TEMPLATES / directory, self.root / directory)
        (self.root / "data/source").mkdir(parents=True)
        (self.root / "pyspark").mkdir()
        (self.root / "pyspark/bronze_silver.py").write_text("# fixture transformation module\n")
        (self.root / "data/source/orders.csv").write_text('OrderKey,Note\n1,"hello\nworld"\n', encoding="utf-8")
        digest = handoff.sha256(self.root / "data/source/orders.csv")
        self.truth = {"datasetFingerprint": hashlib.sha256(f"orders.csv:{digest}".encode()).hexdigest(),
                      "sourceFileSha256": {"orders.csv": digest}, "sourceRowCounts": {"orders": 1},
                      "expectedSilverRowCounts": {"orders": 1}, "expectedKpis": {"order_count": 1}}
        handoff.write_json(self.root / "truth_manifest.json", self.truth)
        handoff.write_json(self.root / "gcp/bigquery_config.json", CONFIG)
        handoff.write_json(self.root / "pipeline.json", {"version": "1.2"})
        handoff.write_json(self.root / "resolved_project.json", {"datasetFingerprint": self.truth["datasetFingerprint"]})
        self.now = datetime(2026, 9, 4, 12, 0, tzinfo=timezone.utc)
        self.order = handoff.package(self.root, "manual__2026-09-04T12:00:00+00:00", now=self.now)

    def tearDown(self):
        self.temp.cleanup()

    def make_result(self):
        prefix = handoff.table_prefix(self.order)
        return {"contractVersion": "1.2", "status": "completed", "executionRuntime": "google-colab-interactive", "workOrderId": self.order["workOrderId"],
                "runId": self.order["runId"], "datasetFingerprint": self.order["datasetFingerprint"],
                "truthManifestSha256": self.order["truthManifestSha256"],
                "startedAt": (self.now + timedelta(minutes=1)).isoformat(),
                "completedAt": (self.now + timedelta(minutes=2)).isoformat(),
                "sourceFileSha256": self.truth["sourceFileSha256"], "sourceRowCounts": {"orders": 1},
                "silverRowCounts": {"orders": 1}, "warehouseRowCounts": {"orders": 1},
                "kpis": {"order_count": "1"}, "warehouse": {"provider": "bigquery", **{key: CONFIG['gcp'][key] for key in ('projectId', 'dataset', 'location')}},
                "loadJobs": {"orders": {"jobId": "forge_load_" + "a" * 48, "tableId": f"example-project.contoso_forge.{prefix}orders",
                                          "state": "DONE", "sourceFormat": "PARQUET", "outputRows": 1, "inputSha256": "b" * 64}},
                "queryJobs": {"orders": "query_count", "kpis": "query_kpis"}}

    def check(self, result):
        return handoff.reconcile(self.root, self.order, result, self.now + timedelta(minutes=3))

    def test_package_contains_real_sources_and_exact_order_without_result(self):
        with zipfile.ZipFile(self.root / "colab/work_package.zip") as package:
            self.assertEqual(package.read("data/source/orders.csv"), (self.root / "data/source/orders.csv").read_bytes())
            self.assertEqual(json.loads(package.read("colab/work_order.json")), self.order)
            self.assertNotIn("colab/result_manifest.json", package.namelist())
        self.assertEqual(handoff.verify_sources(self.root)[2], {"orders": 1})

    def test_scheduler_retry_reuses_identity(self):
        retry = handoff.package(self.root, self.order["runId"], now=self.now + timedelta(minutes=1))
        self.assertEqual(retry, self.order)
        with self.assertRaisesRegex(ValueError, "another run"):
            handoff.package(self.root, "different-run", now=self.now)

    def test_unique_external_state_path_is_supported(self):
        state = self.root / "runs/another"
        order = handoff.package(self.root, "another", state / "work_order.json", state / "work_package.zip", now=self.now)
        self.assertNotEqual(order["workOrderId"], self.order["workOrderId"])
        self.assertNotEqual(handoff.table_prefix(order), handoff.table_prefix(self.order))

    def test_measured_result_reconciles(self):
        self.assertEqual(self.check(self.make_result())["status"], "reconciled")

    def test_mismatched_identity_and_missing_result_fields_fail(self):
        for field in ("workOrderId", "runId", "datasetFingerprint", "truthManifestSha256"):
            with self.subTest(field=field):
                result = self.make_result()
                result[field] = "different"
                with self.assertRaisesRegex(ValueError, "mismatch"):
                    self.check(result)
        for field in ("sourceRowCounts", "silverRowCounts", "warehouseRowCounts", "kpis", "loadJobs", "queryJobs"):
            with self.subTest(field=field):
                result = self.make_result()
                del result[field]
                with self.assertRaises(ValueError):
                    self.check(result)

    def test_counts_kpis_and_non_finite_values_fail(self):
        for section in ("sourceRowCounts", "silverRowCounts", "warehouseRowCounts"):
            result = self.make_result()
            result[section]["orders"] = 2
            with self.assertRaisesRegex(ValueError, "mismatch"):
                self.check(result)
        for value in ("2", "NaN", "Infinity", True):
            result = self.make_result()
            result["kpis"]["order_count"] = value
            with self.assertRaises(ValueError):
                self.check(result)

    def test_stale_and_future_results_fail(self):
        for field, instant in (("startedAt", self.now - timedelta(seconds=1)),
                               ("completedAt", self.now + timedelta(days=2)),
                               ("completedAt", self.now)):
            result = self.make_result()
            result[field] = instant.isoformat()
            with self.assertRaisesRegex(ValueError, "timestamps"):
                self.check(result)
        with self.assertRaisesRegex(ValueError, "expired"):
            handoff.reconcile(self.root, self.order, self.make_result(), self.now + timedelta(days=2))

    def test_missing_source_or_modified_package_fails(self):
        path = self.root / "gcp/reconcile_kpis.sql"
        path.write_text("SELECT 1")
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            self.check(self.make_result())

    def test_modified_source_and_wrong_destination_fail(self):
        result = self.make_result()
        result["warehouse"]["dataset"] = "wrong"
        with self.assertRaisesRegex(ValueError, "destination mismatch"):
            self.check(result)
        (self.root / "data/source/orders.csv").write_text("OrderKey,Note\n2,changed\n")
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            self.check(self.make_result())

    def test_duplicate_json_and_path_escape_are_rejected(self):
        path = self.root / "duplicate.json"
        path.write_text('{"status":"completed","status":"issued"}')
        with self.assertRaisesRegex(ValueError, "Duplicate JSON"):
            handoff.read_json(path)
        with self.assertRaisesRegex(ValueError, "escapes"):
            handoff.safe_path(self.root, "../outside")

    def test_runtime_results_are_observed_queries_not_copied_truth(self):
        # Lightweight Parquet metadata stand-in isolates the cloud orchestration;
        # actual Spark/Parquet execution is a separate integration check.
        silver = self.root / "lake/silver/orders"
        silver.mkdir(parents=True)
        (silver / "part-00000.parquet").write_bytes(b"parquet API fixture")
        pq = types.ModuleType("pyarrow.parquet")
        pq.ParquetFile = lambda _: types.SimpleNamespace(metadata=types.SimpleNamespace(num_rows=1))
        pa = types.ModuleType("pyarrow")
        pa.parquet = pq
        client = FakeClient()
        client.query_kpi = "9"  # Deliberately diverges from truth's 1.
        result_path = self.root / "colab/result_manifest.json"
        with patch.dict(sys.modules, {"pyarrow": pa, "pyarrow.parquet": pq}), \
             patch.object(handoff, "utcnow", return_value=self.now + timedelta(minutes=2)), \
             patch.object(runtime, "utcnow", return_value=self.now + timedelta(minutes=2)):
            with self.assertRaisesRegex(ValueError, "KPI mismatch"):
                runtime.execute(self.root, self.root / "lake/silver", self.root / "colab/work_order.json", result_path, client, API)
        result = handoff.read_json(result_path)
        self.assertEqual(result["kpis"]["order_count"], "9")
        self.assertEqual(result["warehouseRowCounts"], {"orders": 1})
        self.assertEqual(len(client.queries), 2)
        self.assertEqual(client.queries[0][1]["job_config"].maximum_bytes_billed, 1_000_000)


class BigQueryLoadTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="forge-load-tests-")
        self.path = Path(self.temp.name) / "fixture"
        self.path.write_bytes(b"format dispatch unit fixture")

    def tearDown(self):
        self.temp.cleanup()

    def load(self, client, file_format="parquet", **kwargs):
        return runtime.load_native(client, self.path, "example-project.contoso_forge.test_table", file_format, CONFIG, api=API, **kwargs)

    def test_five_native_format_configs_wait_for_successful_jobs(self):
        for file_format, source_format in runtime.FORMATS.items():
            with self.subTest(format=file_format):
                client = FakeClient()
                result = self.load(client, file_format)
                config = client.submissions[0][2]["job_config"]
                self.assertEqual(config.source_format, source_format)
                self.assertEqual(config.write_disposition, "WRITE_EMPTY")
                self.assertEqual(config.max_bad_records, 0)
                self.assertEqual(client.jobs[result["jobId"]].waited, 1)
                self.assertEqual(result["outputRows"], 1)
                if file_format == "csv":
                    self.assertEqual(config.skip_leading_rows, 1)
                    self.assertTrue(config.allow_quoted_newlines)

    def test_retry_reuses_job_and_different_input_gets_new_job_id(self):
        client = FakeClient()
        initial = self.load(client)
        retry = self.load(client)
        self.assertEqual(initial, retry)
        self.assertEqual(len(client.submissions), 1)
        self.path.write_bytes(b"changed bytes")
        changed = self.load(client)
        self.assertNotEqual(initial["jobId"], changed["jobId"])
        self.assertEqual(client.submissions[-1][2]["job_config"].write_disposition, "WRITE_EMPTY")

    def test_ambiguous_submit_recovers_same_job(self):
        client = FakeClient()
        client.ambiguous_submit = True
        result = self.load(client)
        self.assertEqual(len(client.submissions), 1)
        self.assertEqual(result["state"], "DONE")

    def test_format_and_schema_participate_in_idempotency_key(self):
        client = FakeClient()
        csv = self.load(client, "csv")
        jsonl = self.load(client, "jsonl")
        explicit = self.load(client, "csv", schema=[{"name": "id", "type": "INTEGER"}])
        self.assertEqual(len({csv['jobId'], jsonl['jobId'], explicit['jobId']}), 3)

    def test_rejects_open_table_formats_wrong_target_and_missing_guards(self):
        for file_format in ("iceberg", "delta"):
            with self.assertRaisesRegex(ValueError, "Unsupported native"):
                self.load(FakeClient(), file_format)
        with self.assertRaisesRegex(ValueError, "configured dataset"):
            runtime.load_native(FakeClient(), self.path, "other-project.dataset.table", "csv", CONFIG, api=API)
        bad = copy.deepcopy(CONFIG)
        bad["gcp"]["maximumBytesBilled"] = 0
        with self.assertRaisesRegex(ValueError, "query guard"):
            runtime.validate_config(bad)
        bad = copy.deepcopy(CONFIG)
        bad["maximumLocalFileBytes"] = 1
        with self.assertRaisesRegex(ValueError, "exceeds"):
            runtime.load_native(FakeClient(), self.path, "example-project.contoso_forge.test", "csv", bad, api=API)

    def test_failed_job_never_returns_success_evidence(self):
        client = FakeClient()
        evidence = self.load(client)
        client.jobs[evidence["jobId"]].errors = [{"message": "bad schema"}]
        with self.assertRaisesRegex(RuntimeError, "did not complete"):
            self.load(client)


@unittest.skipUnless(os.environ.get("FORGE_TEST_GENERATED_ROOT"), "Optional existing Silver/Google client integration; set FORGE_TEST_GENERATED_ROOT and FORGE_TEST_SILVER_ROOT")
class GeneratedSilverIntegrationTests(unittest.TestCase):
    def test_real_package_parquet_loading_queries_and_offline_result_reconciliation(self):
        import duckdb
        import sqlglot
        from sqlglot import expressions as exp
        import pyarrow.parquet as pq
        generated = Path(os.environ["FORGE_TEST_GENERATED_ROOT"]).resolve()
        silver = Path(os.environ["FORGE_TEST_SILVER_ROOT"]).resolve()
        database = duckdb.connect()

        class LocalWarehouseClient(FakeClient):
            def load_table_from_file(self, source, table, **kwargs):
                count = pq.ParquetFile(source.name).metadata.num_rows
                database.from_parquet(source.name).create_view(table.rsplit(".", 1)[1])
                job = FakeJob(kwargs["job_id"], output_rows=count)
                self.jobs[job.job_id] = job
                self.submissions.append((source.name, table, kwargs))
                return job

            def query(self, sql, **kwargs):
                self.queries.append((sql, kwargs))
                expression = sqlglot.parse_one(sql, read="bigquery")
                for table in expression.find_all(exp.Table):
                    table.set("catalog", None)
                    table.set("db", None)
                cursor = database.execute(expression.sql(dialect="duckdb"))
                columns = [column[0] for column in cursor.description]
                return FakeJob("offline_query_" + str(len(self.queries)), [dict(zip(columns, row)) for row in cursor.fetchall()])

        try:
            with tempfile.TemporaryDirectory(prefix="forge-generated-roundtrip-") as temp:
                root = Path(temp)
                for directory in ("data/source", "pyspark"):
                    shutil.copytree(generated / directory, root / directory)
                for directory in ("gcp", "colab"):
                    shutil.copytree(TEMPLATES / directory, root / directory, ignore=shutil.ignore_patterns("__pycache__"))
                for name in ("truth_manifest.json", "resolved_project.json", "pipeline.json"):
                    shutil.copyfile(generated / name, root / name)
                handoff.write_json(root / "gcp/bigquery_config.json", CONFIG)
                order = handoff.package(root, "offline-roundtrip")
                client = LocalWarehouseClient()
                result_path = root / "colab/result_manifest.json"
                result = runtime.execute(root, silver, root / "colab/work_order.json", result_path, client)
                report = handoff.reconcile(root, order, handoff.read_json(result_path))
                self.assertEqual(report["status"], "reconciled")
                self.assertEqual(len(client.submissions), 13)
                self.assertEqual(len(client.queries), 14)
                expected_order_count = handoff.read_json(root / "truth_manifest.json")["expectedKpis"]["order_count"]
                self.assertEqual(Decimal(result["kpis"]["order_count"]), Decimal(str(expected_order_count)))
                # Both package and returned result obey the published JSON contracts.
                try:
                    import jsonschema
                except ImportError:
                    pass
                else:
                    jsonschema.Draft202012Validator(handoff.read_json(TEMPLATES / "colab/work_order.schema.json")).validate(order)
                    jsonschema.Draft202012Validator(handoff.read_json(TEMPLATES / "colab/result_manifest.schema.json")).validate(result)
        finally:
            database.close()

    def test_google_sql_business_projection_matches_actual_v1_silver(self):
        import duckdb
        import sqlglot
        from sqlglot import expressions as exp
        import pyarrow.parquet as pq
        root = Path(os.environ["FORGE_TEST_GENERATED_ROOT"]).resolve()
        silver = Path(os.environ["FORGE_TEST_SILVER_ROOT"]).resolve()
        truth, _, _ = handoff.verify_sources(root)
        database = duckdb.connect()
        try:
            for table, expected in truth["expectedSilverRowCounts"].items():
                files = sorted((silver / table).glob("*.parquet"))
                self.assertEqual(sum(pq.ParquetFile(path).metadata.num_rows for path in files), expected)
                database.from_parquet([str(path) for path in files]).create_view(table)
            sql = (TEMPLATES / "gcp/reconcile_kpis.sql").read_text()
            sql = sql.replace("{{dataset}}", "example-project.contoso_forge").replace("{{prefix}}", "")
            expression = sqlglot.parse_one(sql, read="bigquery")
            # Translate only SQL dialect and local table qualification; retain all
            # joins, filters, aggregates and arithmetic from generated GoogleSQL.
            for table in expression.find_all(exp.Table):
                table.set("catalog", None)
                table.set("db", None)
            query = expression.sql(dialect="duckdb")
            cursor = database.execute(query)
            observed = dict(zip([column[0] for column in cursor.description], cursor.fetchone()))
            for kpi, expected in truth["expectedKpis"].items():
                self.assertLessEqual(abs(Decimal(str(observed[kpi])) - Decimal(str(expected))), Decimal("0.000001"), kpi)
            print("Actual V1 Silver / translated GoogleSQL KPI results:", json.dumps(observed, default=str, sort_keys=True))
        finally:
            database.close()

    def test_actual_google_client_config_serializes_all_native_formats(self):
        from google.cloud import bigquery
        with tempfile.TemporaryDirectory(prefix="forge-real-config-") as temp:
            path = Path(temp) / "fixture"
            path.write_bytes(b"API config fixture; transport mocked")
            for file_format, source_format in runtime.FORMATS.items():
                client = FakeClient()
                runtime.load_native(client, path, "example-project.contoso_forge.test_table", file_format, CONFIG)
                config = client.submissions[0][2]["job_config"]
                self.assertIsInstance(config, bigquery.LoadJobConfig)
                wire = config.to_api_repr()
                self.assertEqual(wire["load"]["sourceFormat"], source_format)
                self.assertEqual(wire["load"]["writeDisposition"], "WRITE_EMPTY")
            client = FakeClient()
            runtime.query_measured(client, "SELECT 1 AS row_count", CONFIG)
            wire = client.queries[0][1]["job_config"].to_api_repr()
            self.assertEqual(int(wire["query"]["maximumBytesBilled"]), 1_000_000)


if __name__ == "__main__":
    unittest.main(verbosity=2)
