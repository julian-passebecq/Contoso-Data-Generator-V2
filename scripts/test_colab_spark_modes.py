#!/usr/bin/env python3
"""Offline mode, version and API boundary tests; runtime proof is a separate gate."""
import ast
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
COLAB = ROOT / "DatabaseGenerator/Forge/Templates/free_gcp/colab"
sys.path.insert(0, str(COLAB))
import bootstrap_runtime as bootstrap
import spark_session
import storage_adapter
import run_spark


class VersionPolicyTests(unittest.TestCase):
    def test_native_reuses_colab_404_even_when_requested_359(self):
        result = bootstrap.plan_install({"sparkApiMode": "classic", "sparkVersion": "3.5.9"}, "4.0.4")
        self.assertEqual("4.0.4", result["selectedVersion"])
        self.assertEqual([], result["packages"])
        self.assertFalse(result["changesSparkVersion"])

    def test_missing_native_installs_observed_target(self):
        self.assertEqual(["pyspark==4.0.4"], bootstrap.plan_install({}, None)["packages"])

    def test_unknown_native_refuses_implicit_change(self):
        with self.assertRaisesRegex(ValueError, "outside the allowed set"):
            bootstrap.plan_install({}, "4.1.0")

    def test_explicit_pin_reports_change(self):
        result = bootstrap.plan_install({"sparkVersionPolicy": "pinned", "sparkVersion": "3.5.9"}, "4.0.4")
        self.assertTrue(result["changesSparkVersion"])
        self.assertEqual(["pyspark==3.5.9"], result["packages"])

    def test_connect_adds_version_matched_extras(self):
        result = bootstrap.plan_install({"sparkApiMode": "connect-local"}, "4.0.4")
        self.assertEqual(["pyspark[connect]==4.0.4"], result["packages"])
        self.assertFalse(result["changesSparkVersion"])

    def test_connect_rejects_classic_359(self):
        with self.assertRaises(ValueError):
            bootstrap.plan_install({"sparkApiMode": "connect-local", "sparkVersion": "3.5.9"}, "3.5.9")

    @patch.object(spark_session, "installed_version", return_value="4.0.4")
    def test_runtime_pin_never_silently_uses_native(self, _):
        with self.assertRaisesRegex(ValueError, "Pinned PySpark"):
            spark_session.validate_config({"sparkVersionPolicy": "pinned", "sparkVersion": "3.5.9"})

    def test_issued_runtime_settings_cannot_be_overridden(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "colab").mkdir()
            (root / "colab/spark_config.json").write_text(json.dumps({"sparkApiMode": "classic", "sparkVersionPolicy": "colab-native", "sparkVersion": "4.0.4"}))
            order = root / "order.json"
            order.write_text(json.dumps({"contractVersion": "1.3"}))
            with patch.object(run_spark, "validate_order", return_value=({}, {}, {})):
                for override in ({"spark_api_mode": "connect-local"}, {"spark_version_policy": "pinned"}, {"spark_version": "3.5.9"}):
                    with self.subTest(override=override), self.assertRaisesRegex(ValueError, "hashed work-order"):
                        run_spark.run(root, root / "lake", order, **override)

    def test_evidence_cannot_overwrite_bound_package(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            order = root / "order.json"
            order.write_text(json.dumps({"contractVersion": "1.2", "packageFileSha256": {"truth_manifest.json": "digest"}}))
            with patch.object(run_spark, "validate_order", return_value=({}, {}, {})):
                for protected in (order, root / "truth_manifest.json"):
                    with self.subTest(path=protected), self.assertRaisesRegex(ValueError, "must not overwrite"):
                        run_spark.run(root, root / "lake", order, evidence_output=protected)


class StorageAndApiTests(unittest.TestCase):
    def test_notebook_cells_are_valid_python_with_scope_guarded_warehouse(self):
        notebook = json.loads((COLAB / "contoso_free_gcp.ipynb").read_text())
        positions = {cell["id"]: index for index, cell in enumerate(notebook["cells"])}
        self.assertLess(positions["spark-result"], positions["authentication"])
        for cell in notebook["cells"]:
            if cell["cell_type"] != "code":
                continue
            text = "".join(cell["source"])
            ast.parse(text)
            self.assertIsNone(cell["execution_count"])
            self.assertEqual([], cell["outputs"])
            if cell["id"] in ("authentication", "bigquery", "download"):
                self.assertIn("order.get('executionScope', 'spark-and-bigquery') == 'spark-and-bigquery'", text)

    def test_studio_source_schema_reference_resolves_in_repository(self):
        schema = json.loads((ROOT / "schemas/studio-project.schema.json").read_text())
        self.assertTrue((ROOT / "schemas" / schema["properties"]["sourceProject"]["$ref"]).is_file())
        preset = json.loads((ROOT / "schemas/architecture-preset.schema.json").read_text())
        target, fragment = preset["properties"]["defaults"]["$ref"].split("#")
        self.assertTrue((ROOT / "schemas" / target).is_file())
        self.assertEqual("/$defs/settings", fragment)

    def test_client_local_paths_never_pass_remote_boundary(self):
        for path in ("/content/lake", "lake", "file:///content/lake", "C:/lake"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                storage_adapter.lake_path(path, "connect-remote")

    def test_shared_remote_transform_reports_missing_transport(self):
        for path in ("gs://bucket/lake", "s3://bucket/lake", "abfss://lake@account.dfs.core.windows.net/data"):
            with self.subTest(path=path), self.assertRaisesRegex(NotImplementedError, "shared input/metadata"):
                storage_adapter.lake_path(path, "connect-remote")

    def test_local_modes_use_local_paths(self):
        for mode in ("classic", "connect-local"):
            self.assertEqual(Path("lake").resolve(), storage_adapter.lake_path("lake", mode))

    @patch.object(spark_session, "installed_version", return_value="4.0.4")
    def test_remote_endpoint_is_explicit_and_credential_free(self, _):
        for endpoint in (None, "local[2]", "sc://user:secret@host:15002", "sc://host/;token=x"):
            with self.subTest(endpoint=endpoint), self.assertRaises(ValueError):
                spark_session.validate_config({"sparkApiMode": "connect-remote", "sparkRemote": endpoint})
        self.assertEqual("connect-remote", spark_session.validate_config({"sparkApiMode": "connect-remote", "sparkRemote": "sc://host:15002"})[0])

    def test_connect_path_has_no_classic_or_private_api_access(self):
        forbidden = {"sparkContext", "rdd", "_jvm", "_jdf"}
        for name in ("spark_session.py", "run_spark.py", "storage_adapter.py"):
            tree = ast.parse((COLAB / name).read_text())
            self.assertFalse([node.attr for node in ast.walk(tree) if isinstance(node, ast.Attribute) and node.attr in forbidden], name)
        # Existing business transforms are DataFrame-safe; the untouched V1 session factory remains classic.
        tree = ast.parse((ROOT / "DatabaseGenerator/Forge/Templates/customer_satisfaction/pyspark/bronze_silver.py").read_text())
        for node in tree.body:
            if isinstance(node, ast.FunctionDef) and node.name not in ("build_spark", "main"):
                self.assertFalse([item.attr for item in ast.walk(node) if isinstance(item, ast.Attribute) and item.attr in forbidden], node.name)


if __name__ == "__main__":
    unittest.main()
