"""Synthetic adversarial fixtures for custody checks; real execution has its own CI gate."""
import copy
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "DatabaseGenerator/Forge/Templates/v15"))
from common import read, write, sha
from run import identity, state_path
from orchestration import (DAG_ID, PRODUCER, ARTIFACTS, project_files, validate_cosmos,
                           adopt_cosmos_dbt_results, dbt_guard, prepare_run, validate_airflow)


class CustodyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name).resolve()
        self.run_id = "synthetic-first-run"
        self.state = state_path(self.root, self.run_id)
        self.state.mkdir(parents=True)
        write(self.root / "data/source/fixture.json", {"synthetic": True})
        write(self.root / "truth_manifest.json", {"datasetFingerprint": "synthetic-fixture",
              "sourceFileSha256": {"fixture.json": sha(self.root / "data/source/fixture.json")}})
        write(self.root / "project.json", {"product": {"dbtIntegration": "cosmos"}})
        write(self.root / "run_manifest.json", {"files": {"project.json": sha(self.root / "project.json")}})
        write(self.root / "factory/dbt/dbt_project.yml", {"fixture": True})
        write(self.root / "airflow/dags/contoso_forge_cosmos.py", {"fixture": True})
        metadata = {"project_name": "contoso_forge_customer_satisfaction", "project_id": "fixture-project", "invocation_id": "fixture-invocation",
                    "generated_at": "2026-09-05T12:00:02+00:00", "env": {"FORGE_NONCE": "fixture-nonce", "FORGE_RUN_ID": self.run_id}}
        nodes = {f"{kind}.fixture.{i}": {"resource_type": kind, "checksum": {"name": "sha256", "checksum": str(i)}}
                 for kind, count in (("model", 27), ("test", 135)) for i in range(count)}
        self.manifest = {"metadata": metadata, "nodes": nodes}
        self.results = {"metadata": copy.deepcopy(metadata), "results": [{"unique_id": k, "status": "success" if n["resource_type"] == "model" else "pass"} for k, n in nodes.items()]}
        write(self.root / "factory/dbt_manifest.json", self.manifest)
        write(self.state / "warehouse.duckdb", {"syntheticWarehouse": True})
        self.attempt = {"origin": "cosmos-watcher", "runId": self.run_id, "dagId": DAG_ID, "producerTaskId": PRODUCER,
                        "nonce": "fixture-nonce", "startedAt": "2026-09-05T12:00:00+00:00", "identity": identity(self.root),
                        "dagFileSha256": sha(self.root / "airflow/dags/contoso_forge_cosmos.py"),
                        "renderManifestSha256": sha(self.root / "factory/dbt_manifest.json"),
                        "projectFiles": project_files(self.root / "factory/dbt"), "warehouse": str(self.state / "warehouse.duckdb")}
        self.invocation = {**self.attempt, "startedAt": "2026-09-05T12:00:01+00:00", "completedAt": "2026-09-05T12:00:03+00:00",
                           "status": "succeeded", "exitCode": 0, "command": ["fixture-dbt", "build"], "labelAsOf": "2026-09-05T00:00:00Z",
                           "warehouseSha256": sha(self.state / "warehouse.duckdb")}
        self.callback = {**self.attempt, "tryNumber": 1, "capturedAt": "2026-09-05T12:00:04+00:00", "airflowVersion": "3.3.1"}
        (self.state / "cosmos").mkdir()
        (self.state / "cosmos/build-claim.json").write_text("fixture-nonce")
        write(self.state / "run_evidence.json", {"runId": self.run_id, "identity": self.attempt["identity"],
              "stages": {s: {"status": "succeeded", "artifacts": {}} for s in ("verify", "silver", "validate-silver")}})
        self.seal()

    def seal(self):
        # Recompute transport hashes to test semantic checks independently of corruption.
        write(self.state / "cosmos/producer/manifest.json", self.manifest)
        write(self.state / "cosmos/producer/run_results.json", self.results)
        self.invocation["artifacts"] = {n: sha(self.state / "cosmos/producer" / n) for n in ARTIFACTS}
        self.callback["artifacts"] = self.invocation["artifacts"].copy()
        write(self.state / "cosmos/attempt.json", self.attempt)
        write(self.state / "cosmos/invocation.json", self.invocation)
        self.callback.update(attemptSha256=sha(self.state / "cosmos/attempt.json"), invocationSha256=sha(self.state / "cosmos/invocation.json"))
        write(self.state / "cosmos/callback.json", self.callback)

    def validate(self): return validate_cosmos(self.root, self.state, self.run_id)

    def rejects(self, pattern):
        with self.assertRaisesRegex(ValueError, pattern): self.validate()

    def test_valid_whole_project_is_adopted_without_execution(self):
        with patch("subprocess.run") as execute:
            result = adopt_cosmos_dbt_results(self.root, self.state, self.run_id)
            execute.assert_not_called()
        self.assertEqual((result["models"], result["tests"], result["dbtBuildInvocationCount"]), (27, 135, 1))
        for name in ARTIFACTS: self.assertEqual(sha(self.state / "dbt/target" / name), result["artifacts"][name])

    def test_missing_run_results(self):
        (self.state / "cosmos/producer/run_results.json").unlink()
        with self.assertRaises(FileNotFoundError): self.validate()

    def test_tampered_run_results(self):
        write(self.state / "cosmos/producer/run_results.json", {})
        self.rejects("artifact hash mismatch")

    def test_stale_run_nonce_even_with_matching_hashes(self):
        self.results["metadata"]["env"]["FORGE_NONCE"] = "old-run"
        self.seal(); self.rejects("stale or plain")

    def test_another_run_id(self):
        self.results["metadata"]["env"]["FORGE_RUN_ID"] = "other-run"
        self.seal(); self.rejects("stale or plain")

    def test_plain_dbt_substitution(self):
        self.results["metadata"]["env"] = {}
        self.seal(); self.rejects("stale or plain")

    def test_plain_callback_substitution(self):
        self.callback["origin"] = "plain-dbt"
        self.seal(); self.rejects("incorrect invocation binding")

    def test_missing_model(self):
        self.results["results"] = self.results["results"][1:]
        self.seal(); self.rejects("incomplete or duplicate")

    def test_missing_test(self):
        self.results["results"].pop()
        self.seal(); self.rejects("incomplete or duplicate")

    def test_duplicate_result(self):
        self.results["results"].append(self.results["results"][0])
        self.seal(); self.rejects("incomplete or duplicate")

    def test_failed_producer(self):
        self.invocation.update(status="failed", exitCode=1)
        self.seal(); self.rejects("failed producer")

    def test_skipped_test(self):
        self.results["results"][-1]["status"] = "skipped"
        self.seal(); self.rejects("failed or skipped")

    def test_failed_test(self):
        self.results["results"][-1]["status"] = "fail"
        self.seal(); self.rejects("failed or skipped")

    def test_manifest_project_mismatch(self):
        self.manifest["metadata"]["project_id"] = "another-project"
        self.seal(); self.rejects("manifest project mismatch")

    def test_manifest_checksum_mismatch(self):
        self.manifest["nodes"]["model.fixture.0"]["checksum"]["checksum"] = "tampered"
        self.seal(); self.rejects("manifest node checksum mismatch")

    def test_manifest_coverage_mismatch(self):
        self.manifest["nodes"].pop("test.fixture.0")
        self.seal(); self.rejects("manifest coverage")

    def test_invocation_id_mismatch(self):
        self.results["metadata"]["invocation_id"] = "different-invocation"
        self.seal(); self.rejects("invocation ID mismatch")

    def test_timestamp_outside_invocation(self):
        self.results["metadata"]["generated_at"] = "2026-09-04T12:00:00+00:00"
        self.seal(); self.rejects("timestamp outside invocation")

    def test_producer_retry(self):
        self.callback["tryNumber"] = 2
        self.seal(); self.rejects("retries")

    def test_wrong_task(self):
        self.callback["producerTaskId"] = "plain_dbt_task"
        self.seal(); self.rejects("incorrect invocation binding")

    def test_wrong_dag(self):
        self.callback["dagId"] = "another_dag"
        self.seal(); self.rejects("incorrect invocation binding")

    def test_changed_warehouse(self):
        write(self.state / "warehouse.duckdb", {"modified": True})
        self.rejects("warehouse changed")

    def test_wrong_warehouse_path(self):
        self.attempt["warehouse"] = str(self.state / "cosmos.duckdb")
        self.seal(); self.rejects("warehouse changed")

    def test_second_build_evidence_rejected(self):
        write(self.state / "cosmos/duplicate-build.json", {"attempted": True})
        self.rejects("second full dbt build")

    def test_guard_prevents_second_subprocess(self):
        env = {"FORGE_PROJECT_ROOT": str(self.root), "FORGE_AIRFLOW_RUN_ID": self.run_id, "FORGE_AIRFLOW_TASK_ID": PRODUCER,
               "FORGE_COSMOS_NONCE": self.attempt["nonce"], "FORGE_DUCKDB_PATH": self.attempt["warehouse"]}
        with patch.dict(os.environ, env), patch("subprocess.run") as execute:
            with self.assertRaisesRegex(ValueError, "second full dbt build"):
                dbt_guard(["build", "--project-dir", str(self.root / "factory/dbt")])
            execute.assert_not_called()
        self.rejects("second full dbt build")

    def test_plain_build_for_cosmos_forbidden(self):
        from dbt_runtime import build
        with patch("subprocess.run") as execute:
            with self.assertRaisesRegex(ValueError, "second plain dbt build is forbidden"): build(self.root, self.state)
            execute.assert_not_called()

    def test_existing_canonical_target_rejected(self):
        (self.state / "dbt/target").mkdir(parents=True)
        with self.assertRaisesRegex(ValueError, "target already exists"): adopt_cosmos_dbt_results(self.root, self.state, self.run_id)

    def test_new_run_cannot_adopt_previous_state(self):
        with self.assertRaisesRegex(ValueError, "wrong run state"): validate_cosmos(self.root, self.state, "second-run")

    def test_existing_run_cannot_be_prepared_again(self):
        with self.assertRaisesRegex(ValueError, "fresh DagRun ID"): prepare_run(self.root, self.run_id)

    def test_changed_dag(self):
        write(self.root / "airflow/dags/contoso_forge_cosmos.py", {"changed": True})
        self.rejects("DAG changed")

    def test_partial_selector(self):
        self.invocation["command"] += ["--select", "model.fixture.0"]
        self.seal(); self.rejects("partial invocation")

    def test_callback_witness_tampering(self):
        self.callback["invocationSha256"] = "0" * 64
        write(self.state / "cosmos/callback.json", self.callback)
        self.rejects("witness hash mismatch")

    def airflow_fixture(self):
        # Synthetic DB/import witnesses exercise negative gates, not real execution claims.
        task = {"taskId": PRODUCER, "operator": "DbtProducerWatcherOperator", "state": "success", "tryNumber": 1,
                "startedAt": "2026-09-05T12:00:00+00:00", "completedAt": "2026-09-05T12:00:05+00:00"}
        imported = {"dagId": DAG_ID, "dagFileSha256": self.attempt["dagFileSha256"], "importErrorCount": 0,
                    "taskIds": [PRODUCER], "taskOperators": {PRODUCER: task["operator"]}}
        runtime = {"dagId": DAG_ID, "dagRunId": self.run_id, "state": "success", "tasks": [task]}
        write(self.state / "airflow_imports.json", imported)
        write(self.state / "airflow_runtime.json", runtime)
        write(self.state / "airflow-dags-test.log", {"synthetic": True})
        airflow = {"status": "succeeded", "exitCode": 0, "dagId": DAG_ID, "dagRunId": self.run_id, "tasks": [task],
                   "dagFileSha256": self.attempt["dagFileSha256"], "identity": self.attempt["identity"],
                   "runtimeSha256": sha(self.state / "airflow_runtime.json"), "importsSha256": sha(self.state / "airflow_imports.json"),
                   "logSha256": sha(self.state / "airflow-dags-test.log"), "executionMode": "airflow-dags-test", "persistentDeploymentProven": False,
                   "importErrorCount": 0, "failedTaskCount": 0, "skippedTaskCount": 0, "airflowVersion": "3.3.1", "command": ["fixture-airflow", "dags", "test", DAG_ID]}
        write(self.state / "airflow_execution.json", airflow)
        validate_airflow(self.state, self.run_id)
        return airflow

    def reject_airflow(self, airflow, pattern):
        write(self.state / "airflow_execution.json", airflow)
        with self.assertRaisesRegex(ValueError, pattern): validate_airflow(self.state, self.run_id)

    def test_parse_only_is_not_airflow_execution(self):
        airflow = self.airflow_fixture(); airflow["executionMode"] = "dag-parse"
        self.reject_airflow(airflow, "execution scope")

    def test_airflow_wrong_run(self):
        airflow = self.airflow_fixture(); airflow["dagRunId"] = "older-run"
        self.reject_airflow(airflow, "DagRun binding mismatch")

    def test_airflow_wrong_project(self):
        airflow = self.airflow_fixture(); airflow["identity"] = {}
        self.reject_airflow(airflow, "project identity mismatch")

    def test_airflow_failed_cli(self):
        airflow = self.airflow_fixture(); airflow["exitCode"] = 1
        self.reject_airflow(airflow, "did not complete")

    def test_airflow_partial_tasks(self):
        airflow = self.airflow_fixture(); airflow["tasks"] = []
        self.reject_airflow(airflow, "incomplete task")

    def test_airflow_skipped_count(self):
        airflow = self.airflow_fixture(); airflow["skippedTaskCount"] = 1
        self.reject_airflow(airflow, "failed/skipped")

    def test_airflow_forged_task_state(self):
        airflow = self.airflow_fixture(); airflow["tasks"][0]["state"] = "skipped"
        self.reject_airflow(airflow, "incomplete task")

    def test_airflow_runtime_tampering(self):
        airflow = self.airflow_fixture(); write(self.state / "airflow_runtime.json", {})
        with self.assertRaises((ValueError, KeyError)): validate_airflow(self.state, self.run_id)

    def test_airflow_mark_success_bypass(self):
        airflow = self.airflow_fixture(); airflow["command"] += ["--mark-success-pattern", ".*"]
        self.reject_airflow(airflow, "bypassed")

    def test_airflow_local_proof_cannot_claim_persistent_deployment(self):
        airflow = self.airflow_fixture(); airflow["persistentDeploymentProven"] = True
        self.reject_airflow(airflow, "execution scope")

    def test_airflow_invocation_outside_task(self):
        airflow = self.airflow_fixture()
        self.invocation["startedAt"] = "2026-09-04T12:00:00+00:00"
        self.seal()
        self.reject_airflow(airflow, "outside actual producer task")


if __name__ == "__main__": unittest.main()
