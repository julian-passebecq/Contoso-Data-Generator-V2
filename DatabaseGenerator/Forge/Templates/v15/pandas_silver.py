"""Real pandas Bronze/Silver transformations; Arrow is used only for typed Parquet I/O."""
from decimal import Decimal
import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq
from common import write
from silver_contract import sources, contract, arrow_schema, check_csv_header, ATTRIBUTES, SIMPLE


def read_source(path, columns):
    check_csv_header(path, columns)
    kinds = {"int32": "Int32", "int64": "Int64", "string": "string", "boolean": "string",
             "decimal(18,2)": "string", "timestamp_utc": "string"}
    frame = pd.read_csv(path, dtype={c["name"]: kinds[c["type"]] for c in columns},
                        keep_default_na=False, na_values=[""], encoding="utf-8-sig")
    for c in columns:
        name, kind = c["name"], c["type"]
        if kind == "decimal(18,2)":
            frame[name] = frame[name].map(lambda v: None if pd.isna(v) else Decimal(v))
        elif kind == "timestamp_utc":
            frame[name] = pd.to_datetime(frame[name], format="%Y-%m-%dT%H:%M:%SZ", utc=True, errors="raise")
        elif kind == "boolean":
            values = frame[name].str.lower()
            if not values.dropna().isin(["true", "false"]).all():
                raise ValueError("Invalid boolean in " + name)
            frame[name] = values.map({"true": True, "false": False}).astype("boolean")
    return frame


def silver(raw):
    out = {t: raw[t].drop_duplicates().copy() for t in SIMPLE}
    cdc = raw["customer_cdc"].sort_values(["IngestedAt", "Sequence"], kind="stable", na_position="last").drop_duplicates("EventId")
    out["customer_cdc"] = cdc
    base = out["customers"].assign(Sequence=0, Operation="B",
        SourceEventId="BASE-" + out["customers"]["CustomerKey"].astype("string"))
    changes = cdc.rename(columns={"EventTime": "ValidFrom", "EventId": "SourceEventId"})
    fields = ATTRIBUTES + ["ValidFrom", "Sequence", "Operation", "SourceEventId"]
    points = pd.concat([base[fields], changes[fields]], ignore_index=True).sort_values(
        ["CustomerKey", "ValidFrom", "Sequence", "SourceEventId"], kind="stable", na_position="last")
    groups = points.groupby("CustomerKey", dropna=False, sort=False)
    points["ValidTo"] = groups["ValidFrom"].shift(-1)
    points["IsDeleted"] = groups["Operation"].shift(-1).eq("D").fillna(False)
    points["IsCurrent"] = points["ValidTo"].isna() & points["Operation"].ne("D")
    out["customer_scd2"] = points.loc[points["Operation"].ne("D"),
        ATTRIBUTES + ["ValidFrom", "SourceEventId", "ValidTo", "IsCurrent", "IsDeleted"]]
    events = raw["shipment_events"].sort_values(["IngestedAt", "ShipmentKey"], kind="stable", na_position="last").drop_duplicates("ShipmentEventKey").copy()
    events["IngestionLagHours"] = (events["IngestedAt"] - events["EventTime"]).dt.total_seconds() / 3600.0
    events["IsLateArrival"] = events["IngestionLagHours"] > 24
    out["shipment_events"] = events
    valid_shipment = raw["shipments"]["TrackingNumber"].notna() & raw["shipments"]["TrackingNumber"].str.strip(" ").ne("").fillna(False)
    valid_review = raw["reviews"]["Rating"].between(1, 5)
    out["shipments"] = raw["shipments"].loc[valid_shipment]
    out["reviews"] = raw["reviews"].loc[valid_review.fillna(False)]

    def quality(frame, entity, key, rule, bad, evidence):
        return pd.DataFrame({"Entity": entity, "RecordKey": frame[key].astype("string"),
            "Rule": rule, "BadValue": frame[bad].astype("string"), "EvidenceId": evidence})

    out["quality_issues"] = pd.concat([
        quality(raw["shipments"].loc[~valid_shipment], "Shipment", "ShipmentKey", "TrackingNumber not_null", "TrackingNumber", "EV-QUALITY-NULL"),
        quality(raw["reviews"].loc[(~valid_review).fillna(False)], "Review", "ReviewKey", "Rating between 1 and 5", "Rating", "EV-QUALITY-RANGE")], ignore_index=True)
    return out


def transform(root, state):
    source = sources(root)
    raw = {name: read_source(root / "data/source" / e["file"], e["columns"]) for name, e in source.items()}
    outputs = silver(raw)
    governed = contract(root)["tables"]
    counts = {"bronze": {}, "silver": {}}
    for layer, frames in (("bronze", raw), ("silver", outputs)):
        for name, frame in frames.items():
            columns = source[name]["columns"] if layer == "bronze" else governed[name]["columns"]
            path = state / "lake" / layer / name / "part-00000.parquet"
            path.parent.mkdir(parents=True, exist_ok=True)
            pq.write_table(pa.Table.from_pandas(frame, schema=arrow_schema(columns), preserve_index=False, safe=True), path)
            counts[layer][name] = len(frame)
    write(state / "silver_counts.json", counts)
    return {**counts, "adapter": "pandas", "version": pd.__version__, "execution": "pandas-eager-in-memory"}
