#!/usr/bin/env python3
"""Run the validated V1 business transforms in an experimental Colab adapter.

Only Bronze file IO is adapted to Parquet for free-gcp-lab's tableFormat:none.
CDC, SCD2, deduplication and quarantine rules remain in the V1 module.
"""
from __future__ import annotations

import argparse
import importlib.util
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

from work_order import read_json, validate_order
from spark_session import create_session, dataframe_smoke, runtime_environment
from storage_adapter import lake_path


def file_fingerprint(directory):
    files = {str(path.relative_to(directory)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
             for path in sorted(directory.rglob("*.parquet"))}
    if not files:
        raise ValueError(f"No Parquet files found in {directory}")
    fingerprint = hashlib.sha256("\n".join(f"{name}:{files[name]}" for name in sorted(files)).encode()).hexdigest()
    return files, fingerprint


def run(root, lake_root, work_order, spark_api_mode=None, spark_version_policy=None, spark_version=None,
        spark_remote=None, evidence_output=None):
    root = Path(root).resolve()
    order = read_json(work_order)
    truth, sources, source_counts = validate_order(root, order)
    config_path = root / "colab/spark_config.json"
    config = read_json(config_path) if config_path.exists() else {"sparkApiMode": "classic", "sparkVersionPolicy": "colab-native", "sparkVersion": "4.0.4"}
    for key, value in (("sparkApiMode", spark_api_mode), ("sparkVersionPolicy", spark_version_policy),
                       ("sparkVersion", spark_version), ("sparkRemote", spark_remote)):
        if value is not None:
            if order["contractVersion"] == "1.3" and value != config.get(key):
                raise ValueError(f"{key} differs from the hashed work-order configuration; issue a new work order")
            config[key] = value
    mode = config.get("sparkApiMode", "classic")
    if order.get("requestedSparkApiMode", mode) != mode:
        raise ValueError("Requested Spark mode differs from the work order; issue a new work order for this mode")
    evidence_path = Path(evidence_output).resolve() if evidence_output else root / "colab/spark_runtime.json"
    protected = {(root / name).resolve() for name in order["packageFileSha256"]}
    protected.add(Path(work_order).resolve())
    if evidence_path in protected:
        raise ValueError("Spark evidence output must not overwrite the work order or its execution package")
    lake_root = lake_path(lake_root, mode)
    lake_root.mkdir(parents=True, exist_ok=True)
    marker = lake_root / ".contoso-forge-colab-lake"
    if any(lake_root.iterdir()) and (not marker.exists() or marker.read_text() != order["workOrderId"]):
        raise ValueError("Choose an empty lake directory or one owned by this work order")
    marker.write_text(order["workOrderId"], encoding="utf-8")
    raw = lake_root / "raw"
    raw.mkdir(exist_ok=True)
    for name in sources:
        shutil.copyfile(root / "data/source" / name, raw / name)
    module_spec = importlib.util.spec_from_file_location("forge_v1_bronze_silver", root / "pyspark/bronze_silver.py")
    v1 = importlib.util.module_from_spec(module_spec)
    module_spec.loader.exec_module(v1)
    # Adapt only file IO. The V1 session factory is never invoked; its DataFrame business rules are reused unchanged.
    v1.write_delta = v1.write_parquet
    v1.read_bronze = lambda spark, lake, table: spark.read.parquet(str(lake / "bronze" / table))
    evidence = {"contractVersion": "1.3", "status": "failed", **runtime_environment(),
                "workOrderId": order["workOrderId"], "runId": order["runId"],
                "datasetFingerprint": order["datasetFingerprint"], "truthManifestSha256": order["truthManifestSha256"],
                "packageFileSha256": order["packageFileSha256"], "sourceFileSha256": sources, "sourceRowCounts": source_counts,
                "requestedSparkApiMode": mode, "actualSparkApiMode": None, "fallbackReason": None,
                "inputTransport": order.get("inputTransport", "uploaded-work-package"),
                "inputFingerprint": order["datasetFingerprint"], "truthReconciled": False,
                "startedAt": datetime.now(timezone.utc).isoformat()}
    spark = None
    try:
        spark, observed = create_session(config)
        evidence.update(observed)
        print(json.dumps(observed, indent=2), flush=True)
        evidence["dataframeSmoke"] = dataframe_smoke(spark, lake_root)
        v1.bronze(spark, lake_root)
        v1.silver(spark, lake_root, root / "truth_manifest.json")
        evidence["bronzeRowCounts"] = {table: spark.read.parquet(str(lake_root / "bronze" / table)).count() for table in sorted(v1.SCHEMAS)}
        evidence["silverRowCounts"] = {table: spark.read.parquet(str(lake_root / "silver" / table)).count() for table in sorted(truth["expectedSilverRowCounts"])}
        if evidence["bronzeRowCounts"] != source_counts or evidence["silverRowCounts"] != truth["expectedSilverRowCounts"]:
            raise RuntimeError("Written Bronze/Silver counts differ from the truth manifest")
        evidence["bronzeFileSha256"], evidence["bronzeFingerprint"] = file_fingerprint(lake_root / "bronze")
        evidence["silverFileSha256"], evidence["silverFingerprint"] = file_fingerprint(lake_root / "silver")
        evidence["truthReconciled"] = True
        evidence["status"] = "succeeded"
    except Exception as error:
        evidence["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        evidence["completedAt"] = datetime.now(timezone.utc).isoformat()
        evidence_path.parent.mkdir(parents=True, exist_ok=True)
        evidence_path.write_text(json.dumps(evidence, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        if spark is not None:
            spark.stop()
    return evidence


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".")
    parser.add_argument("--lake-root", default="lake")
    parser.add_argument("--work-order", default="colab/work_order.json")
    parser.add_argument("--spark-api-mode", choices=("classic", "connect-local", "connect-remote"))
    parser.add_argument("--spark-version-policy", choices=("colab-native", "pinned"))
    parser.add_argument("--spark-version")
    parser.add_argument("--spark-remote")
    parser.add_argument("--evidence-output")
    args = parser.parse_args()
    run(args.root, args.lake_root, args.work_order, args.spark_api_mode, args.spark_version_policy,
        args.spark_version, args.spark_remote, args.evidence_output)


if __name__ == "__main__":
    main()
