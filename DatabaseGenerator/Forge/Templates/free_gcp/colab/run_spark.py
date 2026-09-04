#!/usr/bin/env python3
"""Run the validated V1 business transforms in an experimental Colab adapter.

Only Bronze file IO is adapted to Parquet for free-gcp-lab's tableFormat:none.
CDC, SCD2, deduplication and quarantine rules remain in the V1 module.
"""
from __future__ import annotations

import argparse
import importlib.util
import shutil
from pathlib import Path

from work_order import read_json, validate_order


def run(root, lake_root, work_order):
    root, lake_root = Path(root).resolve(), Path(lake_root).resolve()
    order = read_json(work_order)
    _, sources, _ = validate_order(root, order)
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
    v1.write_delta = v1.write_parquet
    v1.read_bronze = lambda spark, lake, table: spark.read.parquet(str(lake / "bronze" / table))
    from pyspark.sql import SparkSession
    spark = (SparkSession.builder.master("local[2]").appName("contoso-forge-colab")
             .config("spark.sql.session.timeZone", "UTC")
             .config("spark.sql.shuffle.partitions", "4")
             .config("spark.driver.memory", "2g").getOrCreate())
    spark.sparkContext.setLogLevel("WARN")
    try:
        v1.bronze(spark, lake_root)
        v1.silver(spark, lake_root, root / "truth_manifest.json")
    finally:
        spark.stop()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".")
    parser.add_argument("--lake-root", default="lake")
    parser.add_argument("--work-order", default="colab/work_order.json")
    args = parser.parse_args()
    run(args.root, args.lake_root, args.work_order)


if __name__ == "__main__":
    main()
