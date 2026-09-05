"""DuckDB implementation of the preserved C# source/Spark Silver contract."""
from pathlib import Path
import duckdb
from common import read, write, identifier as ident, literal


def transform(root, state):
    source = root / "data/source"
    model = read(root / "models/source_model.json")
    lake = state / "lake"
    db = duckdb.connect(str(state / "bronze_silver.duckdb"))
    db.execute("SET TimeZone='UTC'")
    types = {"int32": "INTEGER", "int64": "BIGINT", "string": "VARCHAR", "timestamp_utc": "TIMESTAMP", "boolean": "BOOLEAN", "decimal(18,2)": "DECIMAL(18,2)"}
    counts = {"bronze": {}, "silver": {}}
    try:
        for entity in model["entities"]:
            table = Path(entity["file"]).stem
            columns = {c["name"]: types[c["type"]] for c in entity["columns"]}
            # Explicit contract types also handle tiny/empty tables; inference is never authoritative.
            column_sql = "{" + ", ".join(literal(k) + ": " + literal(v) for k, v in columns.items()) + "}"
            db.execute(f"CREATE OR REPLACE TABLE {ident('raw_' + table)} AS SELECT * FROM read_csv(?, header=true, columns={column_sql}, nullstr='', timestampformat='%Y-%m-%dT%H:%M:%SZ')", [str(source / entity["file"])])
            path = lake / "bronze" / table / "part-00000.parquet"
            path.parent.mkdir(parents=True, exist_ok=True)
            db.execute(f"COPY {ident('raw_' + table)} TO {literal(path.as_posix())} (FORMAT PARQUET)")
            counts["bronze"][table] = db.execute(f"SELECT count(*) FROM {ident('raw_' + table)}").fetchone()[0]
        simple = ["customers", "products", "stores", "orders", "order_rows", "returns", "support_tickets"]
        queries = {t: f"SELECT DISTINCT * FROM {ident('raw_' + t)}" for t in simple}
        queries["customer_cdc"] = "SELECT * FROM raw_customer_cdc QUALIFY row_number() OVER (PARTITION BY EventId ORDER BY IngestedAt, Sequence)=1"
        for table, sql in queries.items():
            db.execute(f"CREATE OR REPLACE TABLE {ident(table)} AS {sql}")
        attributes = "CustomerKey, GivenName, Surname, Email, City, CountryCode, LoyaltyTier"
        queries = {
            "customer_scd2": f"""
                WITH points AS (
                  SELECT {attributes}, ValidFrom, 0 AS Sequence, 'B' AS Operation, 'BASE-' || CustomerKey AS SourceEventId FROM customers
                  UNION ALL
                  SELECT {attributes}, EventTime AS ValidFrom, Sequence, Operation, EventId AS SourceEventId FROM customer_cdc
                ), versioned AS (
                  SELECT *, lead(ValidFrom) OVER w AS ValidTo, lead(Operation) OVER w AS ClosedByOperation
                  FROM points WINDOW w AS (PARTITION BY CustomerKey ORDER BY ValidFrom, Sequence, SourceEventId)
                )
                SELECT {attributes}, ValidFrom, SourceEventId, ValidTo,
                  ValidTo IS NULL AND Operation <> 'D' AS IsCurrent,
                  coalesce(ClosedByOperation='D', false) AS IsDeleted
                FROM versioned WHERE Operation <> 'D'
            """,
            "shipment_events": """SELECT *, epoch(IngestedAt-EventTime)/3600.0 AS IngestionLagHours,
                epoch(IngestedAt-EventTime)/3600.0 > 24 AS IsLateArrival FROM raw_shipment_events
                QUALIFY row_number() OVER (PARTITION BY ShipmentEventKey ORDER BY IngestedAt, ShipmentKey)=1""",
            "shipments": "SELECT * FROM raw_shipments WHERE TrackingNumber IS NOT NULL AND trim(TrackingNumber)<>''",
            "reviews": "SELECT * FROM raw_reviews WHERE Rating BETWEEN 1 AND 5",
            "quality_issues": """SELECT 'Shipment' AS Entity, cast(ShipmentKey AS VARCHAR) AS RecordKey,
                'TrackingNumber not_null' AS Rule, TrackingNumber AS BadValue, 'EV-QUALITY-NULL' AS EvidenceId
                FROM raw_shipments WHERE TrackingNumber IS NULL OR trim(TrackingNumber)=''
                UNION ALL SELECT 'Review', cast(ReviewKey AS VARCHAR), 'Rating between 1 and 5', cast(Rating AS VARCHAR), 'EV-QUALITY-RANGE'
                FROM raw_reviews WHERE NOT (Rating BETWEEN 1 AND 5)"""
        }
        for table, sql in queries.items():
            db.execute(f"CREATE OR REPLACE TABLE {ident(table)} AS {sql}")
        for table in sorted(read(root / "truth_manifest.json")["expectedSilverRowCounts"]):
            path = lake / "silver" / table / "part-00000.parquet"
            path.parent.mkdir(parents=True, exist_ok=True)
            # Deterministic ordering allows repeated logical/Parquet comparisons across same-version runs.
            db.execute(f"COPY (SELECT * FROM {ident(table)} ORDER BY ALL) TO {literal(path.as_posix())} (FORMAT PARQUET)")
            counts["silver"][table] = db.execute(f"SELECT count(*) FROM {ident(table)}").fetchone()[0]
        write(state / "silver_counts.json", counts)
        return counts
    finally:
        db.close()


def validate(root, state):
    truth = read(root / "truth_manifest.json")
    with duckdb.connect() as db:
        actual = {table: db.execute("SELECT count(*) FROM read_parquet(?)", [str(state / "lake/silver" / table / "*.parquet")]).fetchone()[0]
                  for table in truth["expectedSilverRowCounts"]}
        bronze = {table: db.execute("SELECT count(*) FROM read_parquet(?)", [str(state / "lake/bronze" / table / "*.parquet")]).fetchone()[0]
                  for table in actual if table in truth["sourceRowCounts"]}
    if actual != truth["expectedSilverRowCounts"]:
        raise ValueError(f"Persisted Silver counts mismatch: {actual}")
    if bronze != truth["sourceRowCounts"]:
        raise ValueError(f"Persisted Bronze counts mismatch: {bronze}")
    return {"status": "reconciled", "bronze": bronze, "silver": actual}
