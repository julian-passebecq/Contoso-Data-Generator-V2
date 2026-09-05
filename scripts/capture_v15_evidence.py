"""Summarize measured local files for a reviewable handoff; never promotes cloud status."""
import argparse
from pathlib import Path
import sys

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "DatabaseGenerator/Forge/Templates/v15"))
from common import read, write, sha, now
from run import verify_artifacts


def artifact(path):
    return {"path": path.resolve().relative_to(REPO).as_posix(), "sha256": sha(path)}


def metrics(path):
    data = read(path)
    if data["status"] != "executed": raise ValueError("Unexecuted ML is not evidence")
    return {**{k: data[k] for k in ("framework", "selectedModel", "selectedBy", "partitions")},
            "models": {name: {split: {k: v for k, v in m.items() if k != "pr_curve"} for split, m in splits.items()}
                       for name, splits in data["models"].items()}, "artifact": artifact(path)}


def local(root, run_id):
    state = root / ".forge/v15" / run_id
    run = read(state / "run_evidence.json")
    if run["status"] != "succeeded": raise ValueError("Incomplete factory run")
    for stage in run["stages"].values():
        if stage["status"] != "succeeded": raise ValueError("Incomplete stage")
        verify_artifacts(state.resolve(), stage)
    build = read(state / "bi/build_evidence.json")
    if build["status"] != "built" or sha(state / build["artifact"]) != build["sha256"]:
        raise ValueError("Missing/changed Evidence production build")
    paths = [root / "run_manifest.json", root / "truth_manifest.json"] + [state / p for p in (
        "run_evidence.json", "reconciliation.json", "dbt_execution.json", "dbt/target/manifest.json", "dbt/target/run_results.json",
        "bi/report_contract.json", "bi/build_evidence.json", "bi/evidence/build/index.html", "bi/evidence/package-lock.json")]
    result = {"runId": run_id, "status": run["status"], "identity": run["identity"], "startedAt": run["startedAt"],
              "completedAt": run["completedAt"], "python": run["python"], "runtimeVersions": run["runtimeVersions"],
              "stages": list(run["stages"]), "dbt": read(state / "dbt_execution.json"),
              "reconciliation": read(state / "reconciliation.json"), "evidenceBuild": build,
              "artifacts": [artifact(p) for p in paths]}
    if (state / "ml/metrics.json").exists():
        result["ml"] = metrics(state / "ml/metrics.json")
        result["artifacts"] += [artifact(p) for p in sorted((state / "ml").iterdir()) if p.is_file()]
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for option in ("bi-root", "ml-root", "spark-ml", "continuation", "output"):
        parser.add_argument("--" + option, type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    continuation = read(args.continuation / "continuation_summary.json")
    if continuation["status"] != "succeeded": raise ValueError("Incomplete Spark continuation")
    result = {"contractVersion": "1.5-evidence-summary", "capturedAt": now(),
              "baseMainSha": "b9fe2b6f8708a57a91d6a6ba4241e4a4a1661b8f",
              "scope": "Actual local Windows and WSL runs. Plans remain not-executed. Raw outputs stay outside Git; hashes bind this summary to retained local files.",
              "localBi": local(args.bi_root, args.run_id), "localMl": local(args.ml_root, args.run_id),
              "sparkMl": metrics(args.spark_ml / "metrics.json"),
              "sameSessionSparkContinuation": {**continuation, "artifacts": [artifact(args.continuation / p) for p in (
                  "colab/spark_runtime.json", "colab/spark_result_manifest.json", "spark_evidence.json",
                  "factory-session/run_evidence.json", "factory-session/dbt_execution.json", "factory-session/reconciliation.json", "factory-session/ml/metrics.json")]},
              "notExecutedThisPass": ["hosted Colab", "native BigQuery", "BQML training", "MotherDuck", "Dive deployment", "Airflow/Cosmos tasks", "Minikube/GitSync deployment", "IaC apply", "Kaggle hosting", "Databricks hosting"]}
    write(args.output, result)
    print(args.output)
