"""Governed Silver metadata shared by adapters and logical comparison, never Gold logic."""
from copy import deepcopy
from pathlib import Path
import pyarrow as pa
from common import read

VERSION = "contoso-silver-logical-v1"
ATTRIBUTES = ["CustomerKey", "GivenName", "Surname", "Email", "City", "CountryCode", "LoyaltyTier"]
SIMPLE = ["customers", "products", "stores", "orders", "order_rows", "returns", "support_tickets"]
TYPES = {"int32": pa.int32(), "int64": pa.int64(), "string": pa.string(),
         "boolean": pa.bool_(), "timestamp_utc": pa.timestamp("us", tz="UTC"),
         "decimal(18,2)": pa.decimal128(18, 2), "float64": pa.float64()}


def sources(root):
    return {Path(e["file"]).stem: e for e in read(Path(root) / "models/source_model.json")["entities"]}


def column(name, kind, nullable=False, **policy):
    return dict(name=name, type=kind, nullable=nullable, **policy)


def contract(root):
    tables = {name: {"columns": deepcopy(e["columns"]), "key": e["primaryKey"], "unique": True}
              for name, e in sources(root).items()}
    tables["shipment_events"]["columns"] += [
        column("IngestionLagHours", "float64", decimalPlaces=9, rounding="half-even",
               nonFinite="canonical-nan-signed-infinity", negativeZero="positive-zero"),
        column("IsLateArrival", "boolean")]
    tables["customer_scd2"] = {"columns": [*deepcopy(tables["customers"]["columns"]),
        column("SourceEventId", "string"), column("ValidTo", "timestamp_utc", True),
        column("IsCurrent", "boolean"), column("IsDeleted", "boolean")],
        "key": ["CustomerKey", "ValidFrom", "SourceEventId"], "unique": True}
    tables["quality_issues"] = {"columns": [column(n, "string", n == "BadValue")
        for n in ("Entity", "RecordKey", "Rule", "BadValue", "EvidenceId")],
        "key": ["Entity", "RecordKey", "Rule", "EvidenceId"], "unique": False}
    return {"version": VERSION, "timestampPrecision": "microsecond", "naiveTimestampZone": "UTC",
            "columnOrder": "contract", "rowOrder": "canonical-key-bytes-then-complete-row-bytes",
            "tables": tables}


def arrow_schema(columns):
    # Parquet optional/required bits differ between writers. Nullability is governed
    # and checked against actual values, not inferred from physical writer metadata.
    return pa.schema([pa.field(c["name"], TYPES[c["type"]]) for c in columns])


def check_csv_header(path, columns):
    import csv
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        if next(csv.reader(stream)) != [c["name"] for c in columns]:
            raise ValueError("CSV header differs from source contract: " + str(path))
