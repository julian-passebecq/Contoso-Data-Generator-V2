"""Recheck retained V1.7 witness bytes before emitting a release ledger."""
import argparse
import os
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--gate", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    gate = args.gate.resolve()
    sys.path.insert(0, str(gate / "duckdb/factory"))
    from common import read, write, sha, now
    from run import identity, verify_artifacts, planned, verify_motherduck_summary
    from parity import compare_runs
    from layers import verify_bronze
    from external_contract import validate_results
    summary = read(gate / "gate.json")
    if summary["workingTreeDirty"]: raise ValueError("Release capture requires a clean measured checkout")
    parity = read(gate / "engine_parity.json")
    engines = {"duckdb", "polars", "pandas", "spark"} if summary["spark"] == "executed" else {"duckdb", "polars", "pandas"}
    if not parity["matched"] or set(parity["runs"]) != engines or len(parity["tables"]) != 13 or parity["repositoryCommit"] != summary["headCommit"]:
        raise ValueError("Incomplete or wrong-revision parity")
    inputs = [{"engine": e, "root": Path(summary["runs"][e]["root"]), "state": Path(summary["runs"][e]["state"])} for e in sorted(engines)]
    checked = compare_runs(inputs, gate / "recaptured_parity.json", summary["headCommit"])
    if not checked["matched"] or any(checked[k] != parity[k] for k in ("tables", "source", "runs")):
        raise ValueError("Retained parity changed")
    runs = {}
    for label, entry in summary["runs"].items():
        root, state = Path(entry["root"]), Path(entry["state"])
        run = read(state / "run_evidence.json")
        if identity(root) != run["identity"] or set(run["stages"]) != set(planned(root)):
            raise ValueError("Changed run identity or compiled stop boundary")
        for stage in run["stages"].values():
            if stage["status"] != "succeeded": raise ValueError("Incomplete local stage")
            verify_artifacts(state, stage)
        if "bronze" in run["stages"]: verify_bronze(root, state)
        verify_motherduck_summary(state)
        if run["status"] != entry["status"]: raise ValueError("Run status changed")
        runs[label] = {"status": run["status"], "runId": run["runId"], "identity": run["identity"], "goal": run["goal"],
            "stopAfter": run["stopAfter"], "engine": run["engine"], "runtimeVersions": run["runtimeVersions"],
            "runEvidenceSha256": sha(state / "run_evidence.json"),
            "stages": {name: {"status": r["result"]["status"], "artifacts": r["artifacts"]} for name, r in run["stages"].items()}}
        for name in ("bronze_execution.json", "dbt_execution.json", "reconciliation.json", "semantic_execution.json", "kaggle_execution.json", "motherduck_execution.json", "huggingface_execution.json"):
            if (state / name).exists(): runs[label][name] = read(state / name)
    for label in ("duckdb", "specific-ml"):
        state = Path(summary["runs"][label]["state"])
        build = read(state / "bi/build_evidence.json")
        if build["status"] != "built" or sha(state / build["artifact"]) != build["sha256"]:
            raise ValueError("Missing or changed Evidence production build")
        runs[label]["evidenceBuild"] = build
    automl = {"status": "not-executed"}
    if summary["localAutoMl"] == "executed":
        state = Path(summary["runs"]["automl-kaggle"]["state"])
        package_file = state / "kaggle/dataset/package_manifest.json"
        package = read(package_file); package["manifestSha256"] = sha(package_file)
        results = gate / "local-automl-results"
        manifest = validate_results(results, package, {"datasetId": "local-fixture/forge", "kernelId": "local-fixture/automl", "datasetVersion": 1})
        automl = {"status": "executed-locally", "isKaggleExecution": False, "manifest": manifest,
            "execution": read(results / "automl_execution.json"), "metrics": read(results / "metrics.json")}
    result = {"contractVersion": "1.7-evidence-summary", "capturedAt": now(),
        "startingMainSha": "ea7c47a6db64de0e91b69b2259c7d48f744e15cc", "implementationCommit": summary["headCommit"],
        "scope": "Retained local run identities, stage/output hashes, actual engine parity, Evidence builds and optional local MLJAR reverified.",
        "parity": parity, "runs": runs, "localAutoMl": automl,
        "externalAccountExecution": {"Kaggle": "not-executed", "MotherDuck": "not-executed", "HuggingFace": "not-published"},
        "unchangedHistoricalBoundaries": ["hosted Colab", "BigQuery/BQML", "Minikube deployment", "Databricks hosting", "IaC apply"]}
    if os.environ.get("GITHUB_RUN_ID"):
        result["actionsCapture"] = {"runId": os.environ["GITHUB_RUN_ID"], "sha": os.environ["GITHUB_SHA"],
            "url": f"https://github.com/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}", "status": "running-at-capture"}
    write(args.output, result)
    print("Verified V1.7 evidence: " + str(args.output))


if __name__ == "__main__": main()
