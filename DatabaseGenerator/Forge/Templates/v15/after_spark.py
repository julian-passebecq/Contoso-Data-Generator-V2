"""Continue in the same local/Colab session from verified Spark Silver to dbt, ML and Evidence."""
import argparse
from pathlib import Path
import shutil
import sys
from common import read, write, sha, now


def run(root, lake, order_path, runtime_path, state):
    root, lake, state = root.resolve(), lake.resolve(), state.resolve()
    if state.exists(): raise ValueError("Choose a fresh continuation state directory")
    sys.path.insert(0, str(root / "colab"))
    from work_order import validate_order, validate_runtime
    order, runtime = read(order_path), read(runtime_path)
    truth, _, _ = validate_order(root, order, allow_completed_expired=True)
    validate_runtime(order, runtime, truth)
    for layer in ("bronze", "silver"):
        for relative, digest in runtime[layer + "FileSha256"].items():
            path = (lake / layer / relative).resolve()
            if not path.is_relative_to(lake / layer) or sha(path) != digest:
                raise ValueError("Actual Spark Parquet differs from measured runtime: " + relative)
    state.mkdir(parents=True)
    for layer in ("bronze", "silver"):
        shutil.copytree(lake / layer, state / "lake" / layer)
    shutil.copyfile(runtime_path, state / "spark_runtime.json")
    write(state / "silver_counts.json", {"bronze": runtime["bronzeRowCounts"], "silver": runtime["silverRowCounts"]})
    evidence = {"contractVersion": "1.5", "runId": order["runId"], "identity": {"datasetFingerprint": truth["datasetFingerprint"],
                "workOrderId": order["workOrderId"], "sparkRuntimeSha256": sha(runtime_path)}, "status": "running", "startedAt": now(), "stages": {}}
    import dbt_runtime
    import duckdb_silver
    from bi_report import build
    def stage(name, function):
        record = {"startedAt": now(), "status": "running"}
        evidence["stages"][name] = record
        write(state / "run_evidence.json", evidence)
        try: record.update(result=function(), status="succeeded", completedAt=now())
        except Exception as error:
            record.update(status="failed", error=str(error), completedAt=now())
            evidence["status"] = "failed"
            raise
        finally: write(state / "run_evidence.json", evidence)
    stage("validate-silver", lambda: duckdb_silver.validate(root, state))
    stage("dbt", lambda: dbt_runtime.build(root, state))
    stage("reconcile", lambda: dbt_runtime.reconcile(root, state))
    config = read(root / "factory/ml/run_config.json")
    if config["enabled"]:
        config["labelAsOf"] = read(state / "dbt_execution.json")["labelAsOf"]
        if config["target"] in ("colab-sklearn", "local-sklearn"):
            from ml_lab import train_frame
            import pandas as pd
            import pyarrow.parquet as pq
            features = state / "lake/gold/ml_customer_dissatisfaction.parquet"
            if pq.read_metadata(features).num_rows * 4096 > config["materializationLimitMb"] * 1024 * 1024:
                raise ValueError("Feature mart exceeds the Colab memory budget; reduce it or use Spark ML")
            stage("ml", lambda: train_frame(pd.read_parquet(features), config, read(root / "factory/ml/spec.json"), state / "ml", {"featureSha256": sha(features), **evidence["identity"]}))
        else:
            from notebook_export import export
            stage("export-ml", lambda: export(root, state))
    stage("bi", lambda: build(root, state, evidence))
    evidence.update(status="succeeded", completedAt=now())
    write(state / "run_evidence.json", evidence)
    return state


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for name in ("root", "lake", "work-order", "spark-runtime", "state"): parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    print(run(args.root, args.lake, args.work_order, args.spark_runtime, args.state))
