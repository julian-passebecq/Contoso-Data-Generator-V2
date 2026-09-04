# artifact-status: validated
"""Contoso Forge V1 Bronze/Silver reference pipeline.

Bronze is written as Delta Lake 3.3.3 on Apache Spark 3.5.9. Silver is
portable Parquet so dbt-duckdb can consume it without a Delta extension.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

from pyspark.sql import DataFrame, SparkSession, Window
from pyspark.sql import functions as F
from pyspark.sql import types as T


STRING = T.StringType()
INTEGER = T.IntegerType()
LONG = T.LongType()
DECIMAL = T.DecimalType(18, 2)
TIMESTAMP = T.TimestampType()
BOOLEAN = T.BooleanType()


def schema(*fields: tuple[str, T.DataType, bool]) -> T.StructType:
    return T.StructType([T.StructField(name, data_type, nullable) for name, data_type, nullable in fields])


SCHEMAS: dict[str, T.StructType] = {
    "customers": schema(
        ("CustomerKey", INTEGER, False), ("GivenName", STRING, False), ("Surname", STRING, False),
        ("Email", STRING, False), ("City", STRING, False), ("CountryCode", STRING, False),
        ("LoyaltyTier", STRING, False), ("ValidFrom", TIMESTAMP, False),
    ),
    "products": schema(
        ("ProductKey", INTEGER, False), ("ProductName", STRING, False), ("Category", STRING, False),
        ("Brand", STRING, False), ("UnitPrice", DECIMAL, False), ("UnitCost", DECIMAL, False),
    ),
    "stores": schema(
        ("StoreKey", INTEGER, False), ("StoreName", STRING, False), ("Channel", STRING, False),
        ("CountryCode", STRING, False),
    ),
    "orders": schema(
        ("OrderKey", LONG, False), ("CustomerKey", INTEGER, False), ("StoreKey", INTEGER, False),
        ("OrderDate", TIMESTAMP, False), ("CurrencyCode", STRING, False), ("OrderStatus", STRING, False),
    ),
    "order_rows": schema(
        ("OrderKey", LONG, False), ("LineNumber", INTEGER, False), ("ProductKey", INTEGER, False),
        ("Quantity", INTEGER, False), ("UnitPrice", DECIMAL, False), ("NetPrice", DECIMAL, False),
        ("UnitCost", DECIMAL, False),
    ),
    "shipments": schema(
        ("ShipmentKey", LONG, False), ("OrderKey", LONG, False), ("Carrier", STRING, False),
        ("TrackingNumber", STRING, True), ("ShippedAt", TIMESTAMP, False), ("PromisedAt", TIMESTAMP, False),
        ("DeliveredAt", TIMESTAMP, False), ("ShipmentStatus", STRING, False),
    ),
    "shipment_events": schema(
        ("ShipmentEventKey", LONG, False), ("ShipmentKey", LONG, False), ("EventType", STRING, False),
        ("EventTime", TIMESTAMP, False), ("IngestedAt", TIMESTAMP, False), ("Location", STRING, False),
    ),
    "returns": schema(
        ("ReturnKey", LONG, False), ("OrderKey", LONG, False), ("CustomerKey", INTEGER, False),
        ("RequestedAt", TIMESTAMP, False), ("Reason", STRING, False), ("ReturnStatus", STRING, False),
        ("RefundAmount", DECIMAL, False),
    ),
    "support_tickets": schema(
        ("TicketKey", LONG, False), ("OrderKey", LONG, False), ("CustomerKey", INTEGER, False),
        ("OpenedAt", TIMESTAMP, False), ("ClosedAt", TIMESTAMP, True), ("Channel", STRING, False),
        ("Topic", STRING, False), ("Priority", STRING, False), ("SatisfactionScore", INTEGER, False),
    ),
    "reviews": schema(
        ("ReviewKey", LONG, False), ("OrderKey", LONG, False), ("CustomerKey", INTEGER, False),
        ("ProductKey", INTEGER, False), ("ReviewedAt", TIMESTAMP, False), ("Rating", INTEGER, False),
        ("ReviewText", STRING, False), ("VerifiedPurchase", BOOLEAN, False),
    ),
    "customer_cdc": schema(
        ("EventId", STRING, False), ("Operation", STRING, False), ("Sequence", INTEGER, False),
        ("CustomerKey", INTEGER, False), ("EventTime", TIMESTAMP, False), ("IngestedAt", TIMESTAMP, False),
        ("GivenName", STRING, False), ("Surname", STRING, False), ("Email", STRING, False),
        ("City", STRING, False), ("CountryCode", STRING, False), ("LoyaltyTier", STRING, False),
    ),
}


def build_spark() -> SparkSession:
    spark = (
        SparkSession.builder.appName("contoso-forge-customer-satisfaction")
        .config("spark.sql.session.timeZone", "UTC")
        .config("spark.sql.shuffle.partitions", "4")
        .config("spark.databricks.delta.snapshotPartitions", "2")
        .getOrCreate()
    )
    spark.sparkContext.setLogLevel("WARN")
    return spark


def read_csv(spark: SparkSession, raw_root: Path, table: str) -> DataFrame:
    return (
        spark.read.option("header", True)
        .option("mode", "FAILFAST")
        .option("timestampFormat", "yyyy-MM-dd'T'HH:mm:ssX")
        .schema(SCHEMAS[table])
        .csv(str(raw_root / f"{table}.csv"))
    )


def write_delta(frame: DataFrame, path: Path) -> None:
    frame.coalesce(1).write.format("delta").mode("overwrite").option("overwriteSchema", "true").save(str(path))


def write_parquet(frame: DataFrame, path: Path) -> None:
    frame.coalesce(1).write.mode("overwrite").parquet(str(path))


def bronze(spark: SparkSession, lake_root: Path) -> None:
    raw_root = lake_root / "raw"
    bronze_root = lake_root / "bronze"
    for table in sorted(SCHEMAS):
        write_delta(read_csv(spark, raw_root, table), bronze_root / table)
        print(f"bronze:{table}:ok")


def read_bronze(spark: SparkSession, lake_root: Path, table: str) -> DataFrame:
    return spark.read.format("delta").load(str(lake_root / "bronze" / table))


def customer_scd2(customers: DataFrame, cdc: DataFrame) -> DataFrame:
    attributes = ["CustomerKey", "GivenName", "Surname", "Email", "City", "CountryCode", "LoyaltyTier"]
    base = customers.select(
        *attributes,
        F.col("ValidFrom"),
        F.lit(0).cast("int").alias("Sequence"),
        F.lit("B").alias("Operation"),
        F.concat(F.lit("BASE-"), F.col("CustomerKey").cast("string")).alias("SourceEventId"),
    )
    changes = cdc.select(
        *attributes,
        F.col("EventTime").alias("ValidFrom"),
        "Sequence",
        "Operation",
        F.col("EventId").alias("SourceEventId"),
    )
    points = base.unionByName(changes)
    window = Window.partitionBy("CustomerKey").orderBy(F.col("ValidFrom"), F.col("Sequence"), F.col("SourceEventId"))
    versioned = (
        points.withColumn("ValidTo", F.lead("ValidFrom").over(window))
        .withColumn("ClosedByOperation", F.lead("Operation").over(window))
        .withColumn("IsCurrent", F.col("ValidTo").isNull() & (F.col("Operation") != F.lit("D")))
        .withColumn(
            "IsDeleted",
            F.coalesce(F.col("ClosedByOperation") == F.lit("D"), F.lit(False)),
        )
        .filter(F.col("Operation") != F.lit("D"))
        .drop("Sequence", "Operation", "ClosedByOperation")
    )
    return versioned


def quality_record(frame: DataFrame, entity: str, key_column: str, rule: str, bad_value, evidence_id: str) -> DataFrame:
    return frame.select(
        F.lit(entity).alias("Entity"),
        F.col(key_column).cast("string").alias("RecordKey"),
        F.lit(rule).alias("Rule"),
        bad_value.cast("string").alias("BadValue"),
        F.lit(evidence_id).alias("EvidenceId"),
    )


def silver(spark: SparkSession, lake_root: Path, truth_manifest: Path) -> None:
    silver_root = lake_root / "silver"
    simple_tables = ["customers", "products", "stores", "orders", "order_rows", "returns", "support_tickets"]
    outputs: dict[str, DataFrame] = {}
    for table in simple_tables:
        outputs[table] = read_bronze(spark, lake_root, table).dropDuplicates()

    cdc_raw = read_bronze(spark, lake_root, "customer_cdc")
    cdc_window = Window.partitionBy("EventId").orderBy(F.col("IngestedAt"), F.col("Sequence"))
    cdc = cdc_raw.withColumn("_rank", F.row_number().over(cdc_window)).filter(F.col("_rank") == 1).drop("_rank")
    outputs["customer_cdc"] = cdc
    outputs["customer_scd2"] = customer_scd2(outputs["customers"], cdc)

    event_raw = read_bronze(spark, lake_root, "shipment_events")
    event_window = Window.partitionBy("ShipmentEventKey").orderBy(F.col("IngestedAt"), F.col("ShipmentKey"))
    outputs["shipment_events"] = (
        event_raw.withColumn("_rank", F.row_number().over(event_window))
        .filter(F.col("_rank") == 1)
        .drop("_rank")
        .withColumn("IngestionLagHours", (F.unix_timestamp("IngestedAt") - F.unix_timestamp("EventTime")) / F.lit(3600.0))
        .withColumn("IsLateArrival", F.col("IngestionLagHours") > F.lit(24.0))
    )

    shipment_raw = read_bronze(spark, lake_root, "shipments")
    invalid_shipments = shipment_raw.filter(F.col("TrackingNumber").isNull() | (F.trim("TrackingNumber") == ""))
    outputs["shipments"] = shipment_raw.filter(F.col("TrackingNumber").isNotNull() & (F.trim("TrackingNumber") != ""))

    review_raw = read_bronze(spark, lake_root, "reviews")
    invalid_reviews = review_raw.filter(~F.col("Rating").between(1, 5))
    outputs["reviews"] = review_raw.filter(F.col("Rating").between(1, 5))

    quality = quality_record(
        invalid_shipments, "Shipment", "ShipmentKey", "TrackingNumber not_null", F.col("TrackingNumber"), "EV-QUALITY-NULL"
    ).unionByName(
        quality_record(invalid_reviews, "Review", "ReviewKey", "Rating between 1 and 5", F.col("Rating"), "EV-QUALITY-RANGE")
    )
    outputs["quality_issues"] = quality

    for table, frame in sorted(outputs.items()):
        write_parquet(frame, silver_root / table)
        print(f"silver:{table}:{frame.count()}")

    with truth_manifest.open("r", encoding="utf-8") as handle:
        expected = json.load(handle)["expectedSilverRowCounts"]
    actual = {table: outputs[table].count() for table in expected}
    mismatches = {table: {"expected": expected[table], "actual": actual[table]} for table in expected if actual[table] != expected[table]}
    if mismatches:
        raise RuntimeError(f"Silver reconciliation failed: {json.dumps(mismatches, sort_keys=True)}")
    print("silver:truth-manifest-reconciliation:ok")


def smoke(spark: SparkSession) -> None:
    actual = spark.range(1, 101).select(F.sum("id").alias("sum")).first()["sum"]
    if actual != 5050:
        raise RuntimeError(f"Spark smoke expected 5050, found {actual}")
    print("spark-smoke:ok")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--stage", choices=("all", "bronze", "silver", "smoke"), default="all")
    parser.add_argument("--lake-root", default=os.environ.get("FORGE_LAKE_ROOT", "/workspace/lake"))
    parser.add_argument(
        "--truth-manifest",
        default=os.environ.get("FORGE_TRUTH_MANIFEST", "/workspace/out/truth_manifest.json"),
    )
    args = parser.parse_args()

    spark = build_spark()
    try:
        if args.stage == "smoke":
            smoke(spark)
        else:
            lake_root = Path(args.lake_root)
            if args.stage in ("all", "bronze"):
                bronze(spark, lake_root)
            if args.stage in ("all", "silver"):
                silver(spark, lake_root, Path(args.truth_manifest))
    finally:
        spark.stop()


if __name__ == "__main__":
    main()
