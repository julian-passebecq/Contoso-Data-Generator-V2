"""V1.7 DuckDB Silver: preserved governed SQL, reading only persisted Bronze."""
from pathlib import Path
import duckdb
from common import read, write, identifier as ident, literal


def transform(root, state):
    lake = state / "lake"
    counts = {"bronze": read(state / "bronze_execution.json")["rowCounts"], "silver": {}}
    db = duckdb.connect(str(state / "bronze_silver.duckdb"))
    db.execute("SET TimeZone='UTC'")
    try:
        for name in counts["bronze"]:
            db.execute(f"CREATE TABLE {ident('raw_' + name)} AS SELECT * FROM read_parquet(?)", [str(lake / "bronze" / name / "*.parquet")])
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
