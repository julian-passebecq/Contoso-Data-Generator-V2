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
    summary = {**{k: data[k] for k in ("framework", "selectedModel", "selectedBy", "partitions")},
            "models": {name: {split: {k: v for k, v in m.items() if k != "pr_curve"} for split, m in splits.items()}
                       for name, splits in data["models"].items()}, "artifact": artifact(path)}
    if "thresholdAnalysis" in data:
        summary["baselineThreshold"] = data["baselineThreshold"]
        summary["thresholdAnalysis"] = {name: {k: ({mk: mv for mk, mv in value.items() if mk != "pr_curve"}
                                                     if k in ("validation", "test") else value)
                                                 for k, value in entry.items() if k != "validationTradeoff"}
                                        for name, entry in data["thresholdAnalysis"].items()}
        summary["curveArtifacts"] = "Full PR curves and validation threshold tradeoffs are in the hash-bound metrics artifact."
    for key in ("sparkVersion", "identity", "completedAt"):
        if key in data: summary[key] = data[key]
    return summary


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
    for option in ("bi-root", "ml-root", "output"):
        parser.add_argument("--" + option, type=Path, required=True)
    for option in ("spark-ml", "continuation", "causal-ml-root", "previous-ledger", "ci-runs"):
        parser.add_argument("--" + option, type=Path)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    result = {"contractVersion": "1.5-evidence-summary", "capturedAt": now(),
              "baseMainSha": "b9fe2b6f8708a57a91d6a6ba4241e4a4a1661b8f",
              "scope": "Actual local Windows and WSL runs. Plans remain not-executed. Raw outputs stay outside Git; hashes bind this summary to retained local files.",
              "localBi": local(args.bi_root, args.run_id), "localMl": local(args.ml_root, args.run_id),
              "notExecutedThisPass": ["hosted Colab", "native BigQuery", "BQML training", "MotherDuck", "Dive deployment", "Airflow/Cosmos tasks", "Minikube/GitSync deployment", "IaC apply", "Kaggle hosting", "Databricks hosting"]}
    if args.causal_ml_root:
        result["causalMl"] = local(args.causal_ml_root, args.run_id)
        result["causalMl"]["generationControls"] = read(args.causal_ml_root / "project.json")["sourceProject"]["generation"]["ml"]
    if args.previous_ledger:
        previous = read(args.previous_ledger)
        result["historicalEvidence"] = {"recapturedThisPass": False, "capturedAt": previous["capturedAt"],
                                        "ledger": artifact(args.previous_ledger),
                                        "scope": "Prior runtime/dependency versions; not re-certified by this hardening pass."}
    if args.spark_ml:
        result["sparkMl"] = metrics(args.spark_ml / "metrics.json")
    if args.continuation:
        continuation = read(args.continuation / "continuation_summary.json")
        if continuation["status"] != "succeeded": raise ValueError("Incomplete Spark continuation")
        result["sameSessionSparkContinuation"] = {**continuation, "artifacts": [artifact(args.continuation / p) for p in (
            "colab/spark_runtime.json", "colab/spark_result_manifest.json", "spark_evidence.json",
            "factory-session/run_evidence.json", "factory-session/dbt_execution.json", "factory-session/reconciliation.json", "factory-session/ml/metrics.json")]}
    result["engineParity"] = {"status": "not-executed", "designNote": "docs/v1.5-hardening.md#logical-engine-parity-todo",
                             "reason": "No canonical logical Spark/DuckDB comparison executed; separate KPI checks are not parity evidence."}
    result["cosmos"] = {"status": "generated-unverified", "dbtInvocations": 2,
                       "promotionGate": "Real task execution with complete, unambiguous invocation-bound model/test results."}
    if args.ci_runs:
        runs = read(args.ci_runs)
        required = {"factory-v15", "free-gcp-contracts", "pipeline-studio-windows", "validate"}
        if {r["name"] for r in runs} != required or len(runs) != 4:
            raise ValueError("Exactly all four workflows are required")
        if len({r["headSha"] for r in runs}) != 1 or any(r["status"] != "completed" or r["conclusion"] != "success" for r in runs):
            raise ValueError("All four workflows must succeed on one commit")
        result["githubActions"] = {"runs": runs, "source": artifact(args.ci_runs)}
    write(args.output, result)
    print(args.output)
