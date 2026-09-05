"""Dedicated real Spark 4.0.4 Silver parity gate using the preserved V1 transforms."""
import argparse
import importlib.metadata
import importlib.util
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--state", type=Path, required=True)
    args = parser.parse_args()
    root, state = args.root.resolve(), args.state.resolve()
    if state.exists(): raise ValueError("Spark parity needs a fresh isolated state directory")
    sys.path.insert(0, str(root / "factory"))
    from common import write, sha, now
    from run import identity
    from silver_contract import contract
    from duckdb_silver import validate
    from pyspark.sql import SparkSession
    current = identity(root)
    state.mkdir(parents=True)
    record = {"contractVersion": "1.6", "runId": "spark-v16", "identity": current, "startedAt": now(),
        "status": "running", "executionScope": "bronze-silver-only", "stages": {},
        "runtimeVersions": {n: importlib.metadata.version(n) for n in ("pyspark", "pyarrow", "duckdb")}}
    spark = None
    try:
        spec = importlib.util.spec_from_file_location("preserved_spark_silver", root / "pyspark/bronze_silver.py")
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        spark = (SparkSession.builder.master("local[2]").appName("contoso-v16-silver-parity")
            .config("spark.sql.session.timeZone", "UTC").config("spark.sql.shuffle.partitions", "2")
            .config("spark.ui.enabled", "false").getOrCreate())
        spark.sparkContext.setLogLevel("WARN")
        if spark.version != "4.0.4": raise ValueError("Spark parity requires pinned Spark 4.0.4")
        record["engine"] = {"name": "spark", "version": spark.version, "runtime": "local-jvm", "apiMode": "classic"}
        lake = state / "lake"
        # Adapt only physical Bronze I/O. All V1 business transforms remain intact.
        for name in sorted(module.SCHEMAS):
            module.write_parquet(module.read_csv(spark, root / "data/source", name), lake / "bronze" / name)
        module.read_bronze = lambda session, lake_root, name: session.read.parquet(str(lake_root / "bronze" / name))
        module.silver(spark, lake, root / "truth_manifest.json")
        result = validate(root, state)
        write(state / "silver_counts.json", {k: result[k] for k in ("bronze", "silver")})
        write(state / "silver_contract.json", contract(root))
        if identity(root) != current: raise ValueError("Spark input identity changed during execution")
        record["stages"]["silver"] = {"status": "succeeded", "result": result, "completedAt": now(),
            "artifacts": {p.relative_to(state).as_posix(): sha(p) for p in state.rglob("*") if p.is_file()}}
        record["status"] = "succeeded"
    except Exception as error:
        record.update(status="failed", error=str(error))
        raise
    finally:
        if spark is not None: spark.stop()
        record["completedAt"] = now()
        write(state / "run_evidence.json", record)
    print("spark-silver:succeeded " + str(state / "run_evidence.json"))


if __name__ == "__main__": main()
