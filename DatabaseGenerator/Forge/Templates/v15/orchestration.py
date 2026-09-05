"""Fail-closed Cosmos artifact custody, using the trusted local run directory.

Integrity/provenance checks are not cryptographic attestation against an attacker
who can rewrite every witness. Validation itself needs no Airflow/dbt imports.
"""
import argparse
from datetime import datetime
import importlib.metadata
import os
from pathlib import Path
import shutil
import subprocess
import uuid
from common import read, write, sha, now
from run import identity, state_path, verify_artifacts

DAG_ID = "contoso_forge_cosmos"
PRODUCER = "dbt_models_and_tests.dbt_producer_watcher"
ARTIFACTS = ("manifest.json", "run_results.json")
EXPECTED_MODELS, EXPECTED_TESTS = 27, 135


def require(condition, message):
    if not condition:
        raise ValueError("Cosmos evidence: " + message)


def project_files(project):
    # Cosmos symlinks model/macro directories into the producer temporary project.
    result = {}
    for directory, folders, files in os.walk(project, followlinks=True):
        folders[:] = sorted(set(folders) - {"target", "logs", "dbt_packages", "__pycache__"})
        for name in sorted(files):
            path = Path(directory) / name
            result[path.relative_to(project).as_posix()] = sha(path)
    return result


def prepare_run(root, run_id):
    from run import execute
    state = state_path(root, run_id)
    require(not state.exists(), "use a fresh DagRun ID; existing state cannot be reused")
    for stage in ("verify", "silver", "validate-silver"):
        execute(root, run_id, stage)
    require(read(root / "project.json")["product"]["dbtIntegration"] == "cosmos", "project did not select Cosmos")
    attempt = {"schemaVersion": 1, "origin": "cosmos-watcher", "runId": run_id, "dagId": DAG_ID,
               "producerTaskId": PRODUCER, "nonce": str(uuid.uuid4()), "startedAt": now(), "identity": identity(root),
               "dagFileSha256": sha(root / "airflow/dags/contoso_forge_cosmos.py"),
               "renderManifestSha256": sha(root / "factory/dbt_manifest.json"),
               "projectFiles": project_files(root / "factory/dbt"), "warehouse": str(state / "warehouse.duckdb")}
    write(state / "cosmos/attempt.json", attempt)
    return attempt


def dbt_guard(arguments):
    """Actual executable called by Cosmos; retain its one subprocess result."""
    root = Path(os.environ["FORGE_PROJECT_ROOT"]).resolve()
    run_id = os.environ["FORGE_AIRFLOW_RUN_ID"]
    state = state_path(root, run_id)
    attempt = read(state / "cosmos/attempt.json")
    require(os.environ["FORGE_COSMOS_NONCE"] == attempt["nonce"] and os.environ["FORGE_AIRFLOW_TASK_ID"] == PRODUCER,
            "only the current producer may invoke dbt")
    require("build" in arguments and not set(arguments).intersection({"--select", "-s", "--exclude", "--selector", "run", "test"}),
            "expected one unfiltered full build")
    require(os.environ["FORGE_DUCKDB_PATH"] == attempt["warehouse"], "wrong warehouse")
    require(identity(root) == attempt["identity"], "changed input identity")
    project = Path(arguments[arguments.index("--project-dir") + 1])
    observed_files = project_files(project)
    # Cosmos passes profiles separately instead of symlinking this file.
    if "profiles.yml" in attempt["projectFiles"]:
        profiles = Path(arguments[arguments.index("--profiles-dir") + 1]) / "profiles.yml"
        observed_files["profiles.yml"] = sha(profiles)
    differences = sorted(k for k in observed_files.keys() | attempt["projectFiles"].keys() if observed_files.get(k) != attempt["projectFiles"].get(k))
    require(not differences, "producer project files differ from compiled input: " + ", ".join(differences))
    try:
        with (state / "cosmos/build-claim.json").open("x", encoding="utf-8") as stream:
            stream.write(attempt["nonce"])
    except FileExistsError:
        write(state / "cosmos/duplicate-build.json", {"at": now(), "arguments": arguments})
        raise ValueError("Cosmos evidence: unexpected second full dbt build")
    executable = os.environ["FORGE_DBT_EXECUTABLE"]
    env = dict(os.environ, DBT_ENV_CUSTOM_ENV_FORGE_NONCE=attempt["nonce"], DBT_ENV_CUSTOM_ENV_FORGE_RUN_ID=run_id)
    record = {"origin": "cosmos-watcher", "runId": run_id, "dagId": DAG_ID, "producerTaskId": PRODUCER,
              "nonce": attempt["nonce"], "startedAt": now(), "command": [executable, *arguments],
              "dbtExecutable": executable, "dbtCoreVersion": importlib.metadata.version("dbt-core"),
              "dbtAdapterVersion": importlib.metadata.version("dbt-duckdb"), "identity": identity(root), "status": "running",
              "labelAsOf": os.environ["FORGE_LABEL_AS_OF"]}
    write(state / "cosmos/invocation.json", record)
    result = subprocess.run(record["command"], env=env, timeout=1800)
    record.update(exitCode=result.returncode, completedAt=now(), status="succeeded" if result.returncode == 0 else "failed")
    record["artifacts"] = {n: sha(project / "target" / n) for n in ARTIFACTS if (project / "target" / n).is_file()}
    if Path(attempt["warehouse"]).is_file(): record["warehouseSha256"] = sha(Path(attempt["warehouse"]))
    write(state / "cosmos/invocation.json", record)
    return result.returncode


def capture_producer(project_dir, context, **kwargs):
    """Cosmos calls this with its producer's temporary whole-project directory."""
    from cosmos.operators.watcher import DbtProducerWatcherOperator
    root = Path(os.environ["FORGE_PROJECT_ROOT"]).resolve()
    ti = context["ti"]
    require(isinstance(context["task"], DbtProducerWatcherOperator) and ti.task_id == PRODUCER and ti.dag_id == DAG_ID,
            "callback is not the Cosmos Watcher producer")
    state = state_path(root, context["run_id"])
    attempt = read(state / "cosmos/attempt.json")
    require(ti.xcom_pull(task_ids="prepare_silver") == {"state": str(state), "nonce": attempt["nonce"]}, "prepare XCom binding mismatch")
    archive = state / "cosmos/producer"
    archive.mkdir(exist_ok=False)
    for name in ARTIFACTS:
        shutil.copyfile(Path(project_dir) / "target" / name, archive / name)
    callback = {"origin": "cosmos-watcher", "dagId": ti.dag_id, "runId": context["run_id"], "producerTaskId": ti.task_id,
                "tryNumber": ti.try_number, "nonce": attempt["nonce"], "capturedAt": now(),
                "airflowVersion": importlib.metadata.version("apache-airflow"),
                "cosmosVersion": importlib.metadata.version("astronomer-cosmos"),
                "invocationSha256": sha(state / "cosmos/invocation.json"), "attemptSha256": sha(state / "cosmos/attempt.json"),
                "artifacts": {n: sha(archive / n) for n in ARTIFACTS}}
    write(state / "cosmos/callback.json", callback)
    validate_cosmos(root, state, context["run_id"])


def validate_cosmos(root, state, run_id):
    attempt = read(state / "cosmos/attempt.json")
    invocation = read(state / "cosmos/invocation.json")
    callback = read(state / "cosmos/callback.json")
    require(state.resolve() == state_path(root, run_id).resolve(), "wrong run state path")
    require(attempt["identity"] == identity(root) == invocation["identity"], "input identity mismatch")
    require(attempt["projectFiles"] == project_files(root / "factory/dbt"), "project files changed")
    require(attempt["renderManifestSha256"] == sha(root / "factory/dbt_manifest.json"), "render manifest changed")
    require(attempt["dagFileSha256"] == sha(root / "airflow/dags/contoso_forge_cosmos.py"), "DAG changed")
    for record in (attempt, invocation, callback):
        require(record["origin"] == "cosmos-watcher" and record["runId"] == run_id and record["dagId"] == DAG_ID
                and record["producerTaskId"] == PRODUCER and record["nonce"] == attempt["nonce"], "incorrect invocation binding")
    require(callback["tryNumber"] == 1, "producer retries are not certified")
    require(invocation["status"] == "succeeded" and invocation["exitCode"] == 0, "failed producer")
    require(callback["invocationSha256"] == sha(state / "cosmos/invocation.json")
            and callback["attemptSha256"] == sha(state / "cosmos/attempt.json"), "callback witness hash mismatch")
    require((state / "cosmos/build-claim.json").read_text() == attempt["nonce"]
            and not (state / "cosmos/duplicate-build.json").exists(), "unexpected second full dbt build")
    require("build" in invocation["command"] and not set(invocation["command"]).intersection({"--select", "-s", "--exclude", "--selector"}), "partial invocation")
    require(attempt["warehouse"] == str(state / "warehouse.duckdb")
            and invocation["warehouseSha256"] == sha(state / "warehouse.duckdb"), "warehouse changed or incorrectly bound")
    for name in ARTIFACTS:
        require(invocation["artifacts"][name] == callback["artifacts"][name] == sha(state / "cosmos/producer" / name), "artifact hash mismatch: " + name)
    manifest = read(state / "cosmos/producer/manifest.json")
    results = read(state / "cosmos/producer/run_results.json")
    rendered = read(root / "factory/dbt_manifest.json")
    require(manifest["metadata"]["project_name"] == rendered["metadata"]["project_name"] == "contoso_forge_customer_satisfaction"
            and manifest["metadata"]["project_id"] == rendered["metadata"]["project_id"], "manifest project mismatch")
    invocation_id = results["metadata"]["invocation_id"]
    require(bool(invocation_id) and manifest["metadata"]["invocation_id"] == invocation_id, "dbt invocation ID mismatch")
    for artifact in (manifest, results):
        require(artifact["metadata"]["env"].get("FORGE_NONCE") == attempt["nonce"]
                and artifact["metadata"]["env"].get("FORGE_RUN_ID") == run_id, "stale or plain dbt artifacts")
        generated = datetime.fromisoformat(artifact["metadata"]["generated_at"].replace("Z", "+00:00"))
        require(datetime.fromisoformat(invocation["startedAt"]) <= generated <= datetime.fromisoformat(invocation["completedAt"]), "artifact timestamp outside invocation")
    require(datetime.fromisoformat(attempt["startedAt"]) <= datetime.fromisoformat(invocation["startedAt"])
            <= datetime.fromisoformat(invocation["completedAt"]) <= datetime.fromisoformat(callback["capturedAt"]), "invalid producer timestamps")
    expected = {k: n for k, n in rendered["nodes"].items() if n["resource_type"] in ("model", "test") and n.get("config", {}).get("enabled", True)}
    actual = {k: n for k, n in manifest["nodes"].items() if n["resource_type"] in ("model", "test") and n.get("config", {}).get("enabled", True)}
    require(actual.keys() == expected.keys(), "manifest coverage differs from compiled project")
    for key in expected:
        require(actual[key]["checksum"] == expected[key]["checksum"], "manifest node checksum mismatch: " + key)
    executed = [r["unique_id"] for r in results["results"] if r["unique_id"] in actual]
    require(len(executed) == len(set(executed)) and set(executed) == set(expected), "incomplete or duplicate model/test coverage")
    require(all(r["status"] in ("success", "pass") for r in results["results"]), "failed or skipped dbt result")
    require(all(r["unique_id"] in actual or (r["unique_id"] in manifest["nodes"] and manifest["nodes"][r["unique_id"]]["resource_type"] == "operation")
                for r in results["results"]), "unexpected dbt result")
    models = sum(n["resource_type"] == "model" for n in actual.values())
    tests = sum(n["resource_type"] == "test" for n in actual.values())
    require((models, tests) == (EXPECTED_MODELS, EXPECTED_TESTS), "expected 27 models and 135 tests")
    evidence = read(state / "run_evidence.json")
    require(evidence["runId"] == run_id and evidence["identity"] == attempt["identity"], "Forge run identity mismatch")
    for stage in ("verify", "silver", "validate-silver"):
        require(evidence["stages"][stage]["status"] == "succeeded", "upstream stage not successful")
        verify_artifacts(state, evidence["stages"][stage])
    return {"status": "tested", "origin": "cosmos-watcher", "dbtInvocationId": invocation_id, "models": models,
            "tests": tests, "failed": 0, "skipped": 0, "dbtBuildInvocationCount": 1,
            "callbackSha256": sha(state / "cosmos/callback.json"), "artifacts": callback["artifacts"]}


def adopt_cosmos_dbt_results(root, state, run_id):
    result = validate_cosmos(root, state, run_id)
    target = state / "dbt/target"
    require(not target.exists(), "canonical dbt target already exists; refuse substituted artifacts")
    target.mkdir(parents=True)
    for name in ARTIFACTS:
        shutil.copyfile(state / "cosmos/producer" / name, target / name)
        require(sha(target / name) == result["artifacts"][name], "adoption copy mismatch")
    result.update(adoptionStatus="adopted", exitCode=0, labelAsOf=read(state / "cosmos/invocation.json")["labelAsOf"])
    write(state / "dbt_execution.json", result)
    return result


def validate_airflow(state, run_id):
    airflow = read(state / "airflow_execution.json")
    runtime = read(state / "airflow_runtime.json")
    imported = read(state / "airflow_imports.json")
    attempt, invocation, callback = (read(state / ("cosmos/" + n + ".json")) for n in ("attempt", "invocation", "callback"))
    require(airflow["status"] == "succeeded" and airflow["exitCode"] == 0 and runtime["state"] == "success", "Airflow did not complete successfully")
    require(airflow["dagId"] == runtime["dagId"] == imported["dagId"] == DAG_ID
            and airflow["dagRunId"] == runtime["dagRunId"] == run_id, "Airflow DagRun binding mismatch")
    require(airflow["dagFileSha256"] == imported["dagFileSha256"] == attempt["dagFileSha256"], "Airflow DAG hash mismatch")
    require(airflow["identity"] == attempt["identity"], "Airflow project identity mismatch")
    require(airflow["runtimeSha256"] == sha(state / "airflow_runtime.json") and airflow["importsSha256"] == sha(state / "airflow_imports.json")
            and airflow["logSha256"] == sha(state / "airflow-dags-test.log"), "Airflow witness hash mismatch")
    require(airflow["executionMode"] == "airflow-dags-test" and airflow["persistentDeploymentProven"] is False
            and airflow["importErrorCount"] == imported["importErrorCount"] == 0, "incorrect execution scope/imports")
    require(airflow["tasks"] == runtime["tasks"] and sorted(t["taskId"] for t in runtime["tasks"]) == imported["taskIds"], "incomplete task evidence")
    require(airflow["failedTaskCount"] == airflow["skippedTaskCount"] == 0 and all(t["state"] == "success" and t["tryNumber"] == 1
            and t["startedAt"] and t["completedAt"] and t["operator"] == imported["taskOperators"][t["taskId"]] for t in runtime["tasks"]), "failed/skipped/retried task")
    require(imported["taskOperators"].get(PRODUCER) == "DbtProducerWatcherOperator", "plain dbt cannot certify Cosmos")
    require(airflow["airflowVersion"] == callback["airflowVersion"], "Airflow version mismatch")
    command = airflow["command"]
    require(command[1:4] == ["dags", "test", DAG_ID] and "--mark-success-pattern" not in command, "DAG test execution was bypassed")
    producer = next(t for t in runtime["tasks"] if t["taskId"] == PRODUCER)
    require(datetime.fromisoformat(producer["startedAt"]) <= datetime.fromisoformat(invocation["startedAt"])
            <= datetime.fromisoformat(invocation["completedAt"]) <= datetime.fromisoformat(callback["capturedAt"])
            <= datetime.fromisoformat(producer["completedAt"]), "invocation outside actual producer task")
    return airflow


def finalize_execution(root, state, run_id, persist=True):
    result = validate_cosmos(root, state, run_id)
    airflow = validate_airflow(state, run_id)
    attempt, invocation, callback = (read(state / ("cosmos/" + n + ".json")) for n in ("attempt", "invocation", "callback"))
    evidence = read(state / "run_evidence.json")
    require(evidence["status"] == "succeeded" and evidence["stages"]["dbt"]["result"]["origin"] == "cosmos-watcher", "missing Cosmos adoption")
    for stage in evidence["stages"].values():
        require(stage["status"] == "succeeded", "downstream stage failed")
        verify_artifacts(state, stage)
    for name in ARTIFACTS:
        require(sha(state / "dbt/target" / name) == result["artifacts"][name], "canonical artifact substitution")
    kpis = read(state / "reconciliation.json")["kpis"]
    require(len(kpis) == 5 and all(k["matched"] for k in kpis.values()), "incomplete KPI reconciliation")
    report = read(state / "bi/build_evidence.json")
    require(report["status"] == "built" and report["sha256"] == sha(state / "bi/evidence/build/index.html")
            and report["runId"] == run_id and report["reportContractSha256"] == sha(state / "bi/report_contract.json"), "Evidence production build mismatch")
    result.update(schemaVersion=1, status="reconciled", cosmosExecutionMode="WATCHER", runId=run_id, dagId=DAG_ID,
                  producerTaskId=PRODUCER, cosmosVersion=callback["cosmosVersion"], airflowVersion=callback["airflowVersion"],
                  dbtExecutable=invocation["dbtExecutable"], dbtCoreVersion=invocation["dbtCoreVersion"], dbtAdapterVersion=invocation["dbtAdapterVersion"],
                  expectedModels=EXPECTED_MODELS, executedModels=result["models"], expectedTests=EXPECTED_TESTS, executedTests=result["tests"],
                  manifestSha256=result["artifacts"]["manifest.json"], runResultsSha256=result["artifacts"]["run_results.json"],
                  warehouse=attempt["warehouse"], warehouseSha256=invocation["warehouseSha256"], identity=attempt["identity"],
                  projectFiles=attempt["projectFiles"], startedAt=invocation["startedAt"], completedAt=invocation["completedAt"],
                  airflowExecutionSha256=sha(state / "airflow_execution.json"), invocationSha256=sha(state / "cosmos/invocation.json"),
                  adoptionStatus="adopted", reconciliationSha256=sha(state / "reconciliation.json"),
                  evidenceBuildSha256=sha(state / "bi/build_evidence.json"), stages={k: v["status"] for k, v in evidence["stages"].items()})
    if persist: write(state / "cosmos_execution.json", result)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("prepare", "finish"))
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    if args.action == "prepare":
        prepare_run(root, args.run_id)
    else:
        from run import execute
        for stage in ("dbt", "reconcile"):
            execute(root, args.run_id, stage)
        config = read(root / "factory/ml/run_config.json")
        if config["enabled"]: execute(root, args.run_id, "ml" if config["target"] == "local-sklearn" else "export-ml")
        execute(root, args.run_id, "bi")
        from build_evidence import build
        build(state_path(root, args.run_id))


if __name__ == "__main__": main()
