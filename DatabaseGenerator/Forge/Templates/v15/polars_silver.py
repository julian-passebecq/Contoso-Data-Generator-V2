"""Polars LazyFrame/expressions for the governed Bronze/Silver contract."""
import polars as pl
from common import write
from silver_contract import sources, contract, check_csv_header, ATTRIBUTES, SIMPLE


def scan_source(path, columns):
    check_csv_header(path, columns)
    # Complete explicit parser schema; temporal/decimal strings are strictly parsed
    # into their contract types below, with no sampled CSV schema inference.
    types = {"int32": pl.Int32, "int64": pl.Int64, "string": pl.String, "boolean": pl.Boolean,
             "decimal(18,2)": pl.String, "timestamp_utc": pl.String}
    frame = pl.scan_csv(path, schema={c["name"]: types[c["type"]] for c in columns}, null_values="")
    return frame.with_columns([
        pl.col(c["name"]).str.strptime(pl.Datetime("us", "UTC"), format="%Y-%m-%dT%H:%M:%SZ", strict=True)
        if c["type"] == "timestamp_utc" else pl.col(c["name"]).cast(pl.Decimal(18, 2), strict=True)
        for c in columns if c["type"] in ("timestamp_utc", "decimal(18,2)")])


def silver(raw):
    out = {t: raw[t].unique() for t in SIMPLE}
    cdc = raw["customer_cdc"].sort(["IngestedAt", "Sequence"], nulls_last=True, maintain_order=True).unique("EventId", keep="first", maintain_order=True)
    out["customer_cdc"] = cdc
    base = out["customers"].select(*ATTRIBUTES, "ValidFrom", pl.lit(0, dtype=pl.Int32).alias("Sequence"),
        pl.lit("B").alias("Operation"), (pl.lit("BASE-") + pl.col("CustomerKey").cast(pl.String)).alias("SourceEventId"))
    changes = cdc.select(*ATTRIBUTES, pl.col("EventTime").alias("ValidFrom"), "Sequence", "Operation", pl.col("EventId").alias("SourceEventId"))
    points = pl.concat([base, changes]).sort(["CustomerKey", "ValidFrom", "Sequence", "SourceEventId"], nulls_last=True)
    versioned = points.with_columns(pl.col("ValidFrom").shift(-1).over("CustomerKey").alias("ValidTo"),
        pl.col("Operation").shift(-1).over("CustomerKey").eq("D").fill_null(False).alias("IsDeleted"))
    out["customer_scd2"] = versioned.with_columns(
        (pl.col("ValidTo").is_null() & pl.col("Operation").ne("D")).alias("IsCurrent")
        ).filter(pl.col("Operation") != "D").select(*ATTRIBUTES, "ValidFrom", "SourceEventId", "ValidTo", "IsCurrent", "IsDeleted")
    events = raw["shipment_events"].sort(["IngestedAt", "ShipmentKey"], nulls_last=True, maintain_order=True).unique("ShipmentEventKey", keep="first", maintain_order=True)
    out["shipment_events"] = events.with_columns(
        ((pl.col("IngestedAt") - pl.col("EventTime")).dt.total_microseconds() / 3_600_000_000.0).alias("IngestionLagHours")
        ).with_columns((pl.col("IngestionLagHours") > 24).alias("IsLateArrival"))
    valid_shipment = pl.col("TrackingNumber").is_not_null() & (pl.col("TrackingNumber").str.strip_chars(" ") != "")
    valid_review = pl.col("Rating").is_between(1, 5)
    out["shipments"] = raw["shipments"].filter(valid_shipment)
    out["reviews"] = raw["reviews"].filter(valid_review)

    def quality(frame, entity, key, rule, bad, evidence):
        return frame.select(pl.lit(entity).alias("Entity"), pl.col(key).cast(pl.String).alias("RecordKey"),
            pl.lit(rule).alias("Rule"), pl.col(bad).cast(pl.String).alias("BadValue"), pl.lit(evidence).alias("EvidenceId"))

    out["quality_issues"] = pl.concat([
        quality(raw["shipments"].filter(valid_shipment.not_().fill_null(True)), "Shipment", "ShipmentKey", "TrackingNumber not_null", "TrackingNumber", "EV-QUALITY-NULL"),
        quality(raw["reviews"].filter(valid_review.not_()), "Review", "ReviewKey", "Rating between 1 and 5", "Rating", "EV-QUALITY-RANGE")])
    return out


def transform(root, state):
    source = sources(root)
    raw = {name: scan_source(root / "data/source" / e["file"], e["columns"]) for name, e in source.items()}
    counts = {"bronze": {}, "silver": {}}
    plans = state / "polars_plans"
    plans.mkdir(parents=True, exist_ok=True)
    for layer, frames in (("bronze", raw), ("silver", silver(raw))):
        for name, lazy in frames.items():
            (plans / f"{layer}-{name}.txt").write_text(lazy.explain(engine="streaming"), encoding="utf-8")
            frame = lazy.collect(engine="streaming")
            path = state / "lake" / layer / name / "part-00000.parquet"
            path.parent.mkdir(parents=True, exist_ok=True)
            frame.write_parquet(path)
            counts[layer][name] = frame.height
    write(state / "silver_counts.json", counts)
    return {**counts, "adapter": "polars", "version": pl.__version__, "requestedCollectEngine": "streaming",
            "execution": "lazy-expressions; supported streaming with possible in-memory operators; final frame materialized",
            "universalStreamingVerified": False, "plans": "polars_plans"}
