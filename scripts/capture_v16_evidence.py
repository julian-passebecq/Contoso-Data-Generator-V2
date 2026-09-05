"""Capture only measured, verified V1.6 results; optional completed Actions ledger."""
import argparse
import os
from pathlib import Path
import sys

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "DatabaseGenerator/Forge/Templates/v15"))
from common import read, write, sha, now
from parity import compare_runs
from run import verify_artifacts


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--gate", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--spark-parity", type=Path)
    parser.add_argument("--ci-runs", type=Path)
    args = parser.parse_args()
    gate = args.gate.resolve()
    parity = read(gate / "engine_parity.json")
    if not parity["matched"] or set(parity["runs"]) != {"duckdb", "polars", "pandas"} or len(parity["tables"]) != 13:
        raise ValueError("Complete three-engine parity required")
    runs = [{"engine": e, "root": gate / e, "state": gate / e / ".forge/v15/v16"} for e in parity["runs"]]
    verified = compare_runs(runs, gate / "recaptured_parity.json", parity["repositoryCommit"])
    if not verified["matched"]: raise ValueError("Retained engine evidence no longer verifies")
    for name, binding in verified["runs"].items():
        if binding != parity["runs"][name]: raise ValueError("Retained run changed since parity")
    engines = {}
    for run in runs:
        state = run["state"]
        build = read(state / "bi/build_evidence.json")
        if build["status"] != "built" or sha(state / build["artifact"]) != build["sha256"]:
            raise ValueError("Missing/changed production Evidence build")
        evidence = read(state / "run_evidence.json")
        for stage in evidence["stages"].values(): verify_artifacts(state, stage)
        dbt = read(state / "dbt_execution.json")
        metrics = read(state / "ml/metrics.json")
        engines[run["engine"]] = {"root": str(run["root"].relative_to(REPO)), "state": str(state.relative_to(REPO)),
            "runtimeVersions": evidence["runtimeVersions"], "python": evidence["python"],
            "runEvidenceSha256": sha(state / "run_evidence.json"), "silver": evidence["stages"]["silver"]["result"],
            "dbt": {k: dbt[k] for k in ("models", "tests", "failed", "skipped", "status")},
            "reconciliation": read(state / "reconciliation.json"), "evidenceBuild": build,
            "ml": {"status": metrics["status"], "selectedModel": metrics["selectedModel"], "selectedBy": metrics["selectedBy"],
                   "partitions": metrics["partitions"], "metricsSha256": sha(state / "ml/metrics.json")}}
    result = {"contractVersion": "1.6-evidence-summary", "capturedAt": now(),
        "startingMainSha": "b004a4d98e65eb9693a144db17f64676470946eb", "implementationCommit": parity["repositoryCommit"],
        "scope": "Measured full local engine pipelines; separate actual Spark Silver evidence when supplied. New plans remain not-executed.",
        "localEngines": engines, "engineParity": {"artifact": str((gate / "engine_parity.json").relative_to(REPO)),
            "sha256": sha(gate / "engine_parity.json"), **parity},
        "sparkParity": {"status": "not-executed"},
        "unverifiedThisRelease": ["hosted Colab", "native BigQuery/BQML", "MotherDuck/Dive", "Airflow/Cosmos tasks", "Kaggle/Databricks hosting", "Minikube/GitSync deployment", "IaC apply"]}
    if args.spark_parity:
        spark = read(args.spark_parity)
        if not spark["matched"] or "spark" not in spark["runs"] or len(spark["tables"]) != 13:
            raise ValueError("Real successful Spark comparison required")
        spark_runs = [{"engine": e, "root": r["root"], "state": r["state"]} for e, r in spark["runs"].items()]
        checked = compare_runs(spark_runs, gate / "recaptured_spark_parity.json", spark["repositoryCommit"])
        if not checked["matched"] or checked["runs"] != spark["runs"]: raise ValueError("Retained Spark evidence changed")
        result["sparkParity"] = {"status": "matched", "sha256": sha(args.spark_parity), **spark}
    if args.ci_runs:
        ci = read(args.ci_runs)
        required = {"factory-v15", "factory-v16", "spark-parity-v16", "free-gcp-contracts", "pipeline-studio-windows", "validate"}
        if {r["name"] for r in ci} != required or len(ci) != len(required) or len({r["headSha"] for r in ci}) != 1:
            raise ValueError("Exactly six workflows on one commit required")
        if any(r["status"] != "completed" or r["conclusion"] != "success" for r in ci): raise ValueError("All workflows must be successful")
        result["githubActions"] = ci
    elif os.environ.get("GITHUB_RUN_ID"):
        result["currentActionsRun"] = {"runId": os.environ["GITHUB_RUN_ID"], "sha": os.environ["GITHUB_SHA"],
            "url": f"https://github.com/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}",
            "status": "running-at-capture; final conclusion must be fetched after completion"}
    write(args.output, result)
    print(args.output)


if __name__ == "__main__": main()
