"""Independently persisted Bronze and Silver; business transforms retain V1.6 semantics."""
from importlib import import_module, metadata, util
from pathlib import Path
from common import read, write, sha, now, identifier, literal
from silver_contract import sources, contract, arrow_schema, check_csv_header


def files(directory):
    return {p.relative_to(directory).as_posix(): sha(p) for p in sorted(directory.rglob("*.parquet"))}


def verify_bronze(root, state):
    evidence = read(state / "bronze_execution.json")
    identity = read(state / "run_evidence.json")
    if evidence["identity"] != identity["identity"] or evidence["runId"] != identity["runId"]:
        raise ValueError("Bronze run identity mismatch")
    if evidence["files"] != files(state / "lake/bronze"):
        raise ValueError("Persisted Bronze artifact set or checksum changed")
    if evidence["rowCounts"] != read(root / "truth_manifest.json")["sourceRowCounts"]:
        raise ValueError("Bronze row counts do not match governed source")
    return evidence


def spark_module(root):
    spec = util.spec_from_file_location("preserved_spark_layers", root / "pyspark/bronze_silver.py")
    module = util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def spark_session():
    from pyspark.sql import SparkSession
    return (SparkSession.builder.master("local[2]").appName("Contoso Forge V1.7")
            .config("spark.sql.session.timeZone", "UTC").config("spark.sql.shuffle.partitions", "2")
            .config("spark.ui.enabled", "false").getOrCreate())


def bronze(root, state, engine):
    source = sources(root)
    lake = state / "lake/bronze"
    if lake.exists(): raise ValueError("Bronze output must be fresh; use a new run after failure")
    start, counts = now(), {}
    if engine == "spark":
        module, spark = spark_module(root), spark_session()
        try:
            for name in sorted(source):
                frame = module.read_csv(spark, root / "data/source", name)
                module.write_parquet(frame, lake / name)
                counts[name] = spark.read.parquet(str(lake / name)).count()
        finally: spark.stop()
    else:
        for name, entity in source.items():
            path = lake / name / "part-00000.parquet"
            path.parent.mkdir(parents=True)
            csv = root / "data/source" / entity["file"]
            check_csv_header(csv, entity["columns"])
            if engine == "polars":
                frame = import_module("polars_silver").scan_source(csv, entity["columns"]).collect(engine="streaming")
                frame.write_parquet(path)
                counts[name] = frame.height
            elif engine == "pandas":
                import pyarrow as pa
                import pyarrow.parquet as pq
                frame = import_module("pandas_silver").read_source(csv, entity["columns"])
                pq.write_table(pa.Table.from_pandas(frame, schema=arrow_schema(entity["columns"]), preserve_index=False, safe=True), path)
                counts[name] = len(frame)
            elif engine == "duckdb":
                import duckdb
                types = {"int32": "INTEGER", "int64": "BIGINT", "string": "VARCHAR", "timestamp_utc": "TIMESTAMP", "boolean": "BOOLEAN", "decimal(18,2)": "DECIMAL(18,2)"}
                columns = "{" + ", ".join(literal(c["name"]) + ": " + literal(types[c["type"]]) for c in entity["columns"]) + "}"
                with duckdb.connect() as db:
                    db.execute(f"CREATE TABLE raw AS SELECT * FROM read_csv(?, header=true, columns={columns}, nullstr='', timestampformat='%Y-%m-%dT%H:%M:%SZ')", [str(csv)])
                    db.execute("COPY raw TO ? (FORMAT PARQUET)", [str(path)])
                    counts[name] = db.execute("SELECT count(*) FROM raw").fetchone()[0]
            else: raise ValueError("Unsupported Bronze engine")
    if counts != read(root / "truth_manifest.json")["sourceRowCounts"]: raise ValueError("Bronze count mismatch")
    run = read(state / "run_evidence.json")
    result = {"status": "executed", "runId": run["runId"], "identity": run["identity"], "adapter": engine,
              "version": metadata.version("pyspark" if engine == "spark" else engine), "rowCounts": counts,
              "files": files(lake), "startedAt": start, "completedAt": now()}
    write(state / "bronze_execution.json", result)
    return result


def silver(root, state, engine):
    bronze_record = verify_bronze(root, state)
    if (state / "lake/silver").exists(): raise ValueError("Silver output must be fresh")
    counts = {"bronze": bronze_record["rowCounts"], "silver": {}}
    if engine == "duckdb":
        counts = import_module("duckdb_staged_silver").transform(root, state)
    elif engine == "spark":
        module, spark = spark_module(root), spark_session()
        try:
            module.read_bronze = lambda session, lake, name: session.read.parquet(str(lake / "bronze" / name))
            module.silver(spark, state / "lake", root / "truth_manifest.json")
            counts = {k: v for k, v in import_module("duckdb_silver").validate(root, state).items() if k in counts}
        finally: spark.stop()
    else:
        module = import_module(engine + "_silver")
        if engine == "polars":
            import polars as pl
            raw = {name: pl.scan_parquet(state / "lake/bronze" / name / "*.parquet") for name in sources(root)}
        elif engine == "pandas":
            import pandas as pd
            import pyarrow as pa
            import pyarrow.parquet as pq
            raw = {name: pd.read_parquet(state / "lake/bronze" / name) for name in sources(root)}
        else: raise ValueError("Unsupported Silver engine")
        for name, frame in module.silver(raw).items():
            path = state / "lake/silver" / name / "part-00000.parquet"
            path.parent.mkdir(parents=True)
            if engine == "polars":
                frame = frame.collect(engine="streaming")
                frame.write_parquet(path)
                counts["silver"][name] = frame.height
            else:
                pq.write_table(pa.Table.from_pandas(frame, schema=arrow_schema(contract(root)["tables"][name]["columns"]), preserve_index=False, safe=True), path)
                counts["silver"][name] = len(frame)
    verify_bronze(root, state)
    write(state / "silver_counts.json", counts)
    write(state / "silver_contract.json", contract(root))
    return {**counts, "adapter": engine, "version": metadata.version("pyspark" if engine == "spark" else engine), "input": "persisted-bronze"}
