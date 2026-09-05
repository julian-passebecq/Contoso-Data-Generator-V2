"""Capture an exact green CI revision and verify its downloaded orchestration witnesses.

The CI gate validates live inputs/warehouse. This release capture verifies the
retained artifacts; it does not claim to reexecute Airflow or an omitted warehouse.
"""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

WORKFLOWS = {"validate", "pipeline-studio-windows", "free-gcp-contracts", "factory-v15", "factory-v16", "spark-parity-v16", "orchestration-v16"}


def read(path): return json.loads(path.read_text(encoding="utf-8-sig"))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition: raise ValueError(message)


def capture(artifacts, ci, revision, inventory, archives):
    require({r["name"] for r in ci} == WORKFLOWS and len(ci) == 7, "Expected exactly seven workflow records")
    require(all(r["headSha"] == revision and r["status"] == "completed" and r["conclusion"] == "success" for r in ci), "CI must be green on the recorded revision")
    orchestration_id = next(r["databaseId"] for r in ci if r["name"] == "orchestration-v16")
    require({a["name"] for a in inventory["artifacts"]} == {"v16-orchestration-ml", "v16-orchestration-bi", "v16-orchestration-export"}, "Wrong artifact inventory")
    for artifact in inventory["artifacts"]:
        require(artifact["workflow_run"]["id"] == orchestration_id and artifact["workflow_run"]["head_sha"] == revision
                and artifact["expired"] is False, "Artifact is not from the recorded CI revision/run")
        archive = archives / (artifact["name"] + ".zip")
        require("sha256:" + sha(archive) == artifact["digest"], "Downloaded archive digest mismatch")
        with zipfile.ZipFile(archive) as zipped:
            for entry in zipped.infolist():
                if entry.is_dir(): continue
                target = (artifacts / artifact["name"] / entry.filename).resolve()
                require(target.is_relative_to(artifacts.resolve()), "Unsafe archive entry")
                require(sha(target) == hashlib.sha256(zipped.read(entry)).hexdigest(), "Extracted archive changed: " + entry.filename)
    paths = sorted(artifacts.rglob("cosmos_execution.json"))
    require(len(paths) == 4, "Expected ML, export and two no-ML DagRuns")
    runs, invocations, nonces = [], set(), set()
    for path in paths:
        state = path.parent
        cosmos = read(path)
        airflow = read(state / "airflow_execution.json")
        runtime = read(state / "airflow_runtime.json")
        imported = read(state / "airflow_imports.json")
        attempt = read(state / "cosmos/attempt.json")
        invocation = read(state / "cosmos/invocation.json")
        callback = read(state / "cosmos/callback.json")
        evidence = read(state / "run_evidence.json")
        build = read(state / "bi/build_evidence.json")
        manifest = read(state / "dbt/target/manifest.json")
        results = read(state / "dbt/target/run_results.json")
        require(cosmos["status"] == "reconciled" and cosmos["origin"] == "cosmos-watcher" and cosmos["cosmosExecutionMode"] == "WATCHER", "Not reconciled Cosmos")
        require(cosmos["runId"] == airflow["dagRunId"] == runtime["dagRunId"] == attempt["runId"] == invocation["runId"] == callback["runId"] == evidence["runId"] == build["runId"], "Run binding mismatch")
        require(cosmos["identity"] == airflow["identity"] == evidence["identity"] == attempt["identity"] == invocation["identity"], "Input binding mismatch")
        require(airflow["status"] == "succeeded" and airflow["exitCode"] == 0 and runtime["state"] == "success", "Airflow failed")
        require(airflow["tasks"] == runtime["tasks"] and sorted(t["taskId"] for t in runtime["tasks"]) == imported["taskIds"], "Incomplete task set")
        require(all(t["state"] == "success" and t["tryNumber"] == 1 and t["startedAt"] and t["completedAt"] for t in runtime["tasks"]), "Failed/skipped/retried tasks")
        require(airflow["failedTaskCount"] == airflow["skippedTaskCount"] == airflow["importErrorCount"] == imported["importErrorCount"] == 0, "Import or task failures")
        require(airflow["executionMode"] == "airflow-dags-test" and airflow["persistentDeploymentProven"] is False, "Unsupported scope claim")
        links = [(cosmos, "airflowExecutionSha256", "airflow_execution.json"), (cosmos, "invocationSha256", "cosmos/invocation.json"),
                 (cosmos, "callbackSha256", "cosmos/callback.json"), (cosmos, "reconciliationSha256", "reconciliation.json"),
                 (cosmos, "evidenceBuildSha256", "bi/build_evidence.json"), (airflow, "runtimeSha256", "airflow_runtime.json"),
                 (airflow, "importsSha256", "airflow_imports.json"), (airflow, "logSha256", "airflow-dags-test.log"),
                 (callback, "attemptSha256", "cosmos/attempt.json"), (callback, "invocationSha256", "cosmos/invocation.json"),
                 (build, "reportContractSha256", "bi/report_contract.json"), (build, "sha256", "bi/evidence/build/index.html")]
        for record, key, name in links: require(record[key] == sha(state / name), "Witness hash mismatch: " + name)
        require(cosmos["dbtBuildInvocationCount"] == 1 and not (state / "cosmos/duplicate-build.json").exists(), "Duplicate dbt build")
        require(invocation["status"] == "succeeded" and invocation["exitCode"] == 0 and "build" in invocation["command"], "Producer failed")
        require(cosmos["warehouseSha256"] == invocation["warehouseSha256"] and cosmos["warehouse"] == attempt["warehouse"], "Warehouse binding mismatch")
        for name in ("manifest.json", "run_results.json"):
            digest = sha(state / "dbt/target" / name)
            require(digest == sha(state / "cosmos/producer" / name) == cosmos["artifacts"][name] == invocation["artifacts"][name] == callback["artifacts"][name], "dbt artifact mismatch")
        for artifact in (manifest, results):
            require(artifact["metadata"]["invocation_id"] == cosmos["dbtInvocationId"] and artifact["metadata"]["env"]["FORGE_NONCE"] == attempt["nonce"]
                    and artifact["metadata"]["env"]["FORGE_RUN_ID"] == cosmos["runId"], "Invocation binding mismatch")
        expected = {k: n for k, n in manifest["nodes"].items() if n["resource_type"] in ("model", "test") and n.get("config", {}).get("enabled", True)}
        executed = [r["unique_id"] for r in results["results"] if r["unique_id"] in expected]
        require(len(executed) == len(set(executed)) and set(executed) == set(expected) and all(r["status"] in ("success", "pass") for r in results["results"]), "Incomplete model/test coverage")
        require(sum(n["resource_type"] == "model" for n in expected.values()) == cosmos["executedModels"] == 27
                and sum(n["resource_type"] == "test" for n in expected.values()) == cosmos["executedTests"] == 135, "Wrong coverage")
        kpis = read(state / "reconciliation.json")["kpis"]
        require(len(kpis) == 5 and all(k["matched"] for k in kpis.values()) and build["status"] == "built" and evidence["status"] == "succeeded", "Downstream evidence failed")
        for stage in evidence["stages"].values():
            require(stage["status"] == "succeeded", "Failed factory stage")
            for name, digest in stage["artifacts"].items():
                candidate = (state / name).resolve()
                require(candidate.is_relative_to(state.resolve()), "Unsafe artifact path")
                if candidate.is_file(): require(sha(candidate) == digest, "Retained stage artifact changed: " + name)
        downstream = "ml" if "ml" in evidence["stages"] else "export" if "export-ml" in evidence["stages"] else "bi"
        if downstream == "export": require(evidence["stages"]["export-ml"]["result"]["status"] == "exported-not-executed", "Export mislabeled as training")
        hashes = {name: sha(state / name) for name in ("airflow_execution.json", "cosmos_execution.json", "run_evidence.json", "dbt_execution.json",
                  "cosmos/attempt.json", "cosmos/invocation.json", "cosmos/callback.json", "dbt/target/manifest.json", "dbt/target/run_results.json",
                  "reconciliation.json", "bi/build_evidence.json", "bi/evidence/build/index.html")}
        runs.append({"downstream": downstream, "artifactPath": path.relative_to(artifacts).as_posix(), "cosmos": cosmos,
                     "airflow": {k: v for k, v in airflow.items() if k != "tasks"}, "taskCount": len(runtime["tasks"]),
                     "artifactSha256": hashes, "dbtCommand": invocation["command"], "runtimeVersions": evidence["runtimeVersions"],
                     "python": evidence["python"], "nodeVersion": build["nodeVersion"], "kpis": kpis,
                     "downstreamResult": evidence["stages"].get("ml", evidence["stages"].get("export-ml", {})).get("result")})
        invocations.add(cosmos["dbtInvocationId"]); nonces.add(attempt["nonce"])
    require(len(invocations) == len(nonces) == 4, "Runs reused invocation evidence")
    require(sorted(r["downstream"] for r in runs) == ["bi", "bi", "export", "ml"], "Missing downstream variant")
    return {"schemaVersion": 1, "productVersion": "1.6", "status": "reconciled", "implementationCommit": revision,
            "baseMain": "35d1959c6ecb891b50ba45a47f44d373f76acac7", "scope": "Four complete local Airflow dags-test DagRuns in CI; no persistent scheduler or Kubernetes deployment",
            "captureScope": "Retained CI witnesses verified by hash and identity. Live warehouse/source/Silver checks ran inside the gate; those large files are not in this archive.",
            "tests": {"dotnetPassed": 250, "legacyArtifactsPreserved": 152, "orchestrationPythonPassed": 43,
                      "priorDistinctPythonPassed": 123, "optionalGoogleIntegrationSkipped": 3},
            "ciRuns": ci, "ciArtifacts": inventory["artifacts"], "runs": runs,
            "limitations": ["Only V1.6 DuckDB/Cosmos is promoted; other Cosmos engine/product combinations remain generated",
                            "No persistent scheduler, managed Airflow or new Kubernetes deployment proof",
                            "No retry/consumer fallback certification; start a new DagRun after failure",
                            "Historical and hosted/cloud evidence remains historical; export-ML is not hosted training",
                            "Checks assume a trusted local runtime directory, not adversarial remote attestation"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifacts", required=True, type=Path)
    parser.add_argument("--ci-runs", required=True, type=Path)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--artifact-metadata", required=True, type=Path)
    parser.add_argument("--archives", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = capture(args.artifacts.resolve(), read(args.ci_runs), args.revision, read(args.artifact_metadata), args.archives.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print("Captured four invocation-bound DagRuns and seven green workflows:", args.output)
