#!/usr/bin/env python3
"""Native local-file BigQuery loads and measured warehouse reconciliation.

artifact-status: generated-reference (no live cloud execution claimed).
Authenticate through Application Default Credentials or Colab user auth.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "colab"))
from work_order import (read_json, write_json, sha256, validate_order, reconcile,
                        table_prefix, utcnow, validate_runtime)

FORMATS = {"csv": "CSV", "jsonl": "NEWLINE_DELIMITED_JSON", "avro": "AVRO", "orc": "ORC", "parquet": "PARQUET"}


def validate_config(config):
    gcp = config["gcp"]
    if not re.fullmatch(r"[a-z][a-z0-9-]{4,61}[a-z0-9]", gcp["projectId"]):
        raise ValueError("Set a valid gcp.projectId before executing BigQuery jobs")
    if gcp["projectId"] == "your-gcp-project":
        raise ValueError("Replace your-gcp-project with your actual project ID before packaging")
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,1023}", gcp["dataset"]):
        raise ValueError("Invalid BigQuery dataset identifier")
    if not re.fullmatch(r"[A-Za-z0-9-]+", gcp["location"]):
        raise ValueError("Invalid BigQuery location")
    limit = gcp.get("maximumBytesBilled")
    if type(limit) is not int or limit <= 0:
        raise ValueError("A positive maximumBytesBilled query guard is required")
    file_limit = config.get("maximumLocalFileBytes", 100_000_000)
    if type(file_limit) is not int or file_limit <= 0:
        raise ValueError("A positive maximumLocalFileBytes upload guard is required")
    return gcp


def _status_code(error):
    code = getattr(error, "code", None)
    return code() if callable(code) else code


def preflight(client, config, table_ids=(), create_dataset=False, api=None):
    """Check authorized dataset access/location before uploading any local file.

    This uses native loads and SELECT queries only, with no GCS or billing setup.
    Dataset creation is explicit and keeps the Sandbox 60-day table lifetime.
    """
    gcp = validate_config(config)
    dataset_id = f"{gcp['projectId']}.{gcp['dataset']}"
    try:
        dataset = client.get_dataset(dataset_id)
    except Exception as error:
        code = _status_code(error)
        if code == 404:
            if not create_dataset:
                raise ValueError("BigQuery dataset is missing. Create it in the configured location using the Sandbox console/OpenTofu, or run preflight --create-dataset.") from None
            if api is None:
                from google.cloud import bigquery as api
            dataset = api.Dataset(dataset_id)
            dataset.location = gcp["location"]
            dataset.default_table_expiration_ms = 60 * 24 * 60 * 60 * 1000
            dataset = client.create_dataset(dataset, exists_ok=True)
        elif code in (401, 403) or type(error).__name__ in ("DefaultCredentialsError", "RefreshError"):
            raise RuntimeError("BigQuery authentication/access preflight failed. In hosted Colab run auth.authenticate_user(); locally refresh Application Default Credentials. Grant BigQuery Job User and dataset Data Editor access.") from None
        else:
            raise
    if str(dataset.location).upper() != gcp["location"].upper():
        raise ValueError(f"BigQuery dataset location mismatch: configured {gcp['location']}, observed {dataset.location}")
    tables = {}
    for table_id in table_ids:
        if not table_id.startswith(dataset_id + "."):
            raise ValueError("Preflight table is outside the configured dataset")
        try:
            table = client.get_table(table_id)
        except Exception as error:
            if _status_code(error) == 404:
                tables[table_id] = "absent-create-if-needed"
                continue
            raise
        if getattr(table, "table_type", None) != "TABLE" or getattr(table, "external_data_configuration", None) is not None:
            raise ValueError(f"BigQuery native load target is not a native table: {table_id}")
        tables[table_id] = "existing-write-empty-or-identical-job-retry"
    return {"status": "ready", "datasetId": dataset_id, "location": dataset.location,
            "defaultTableExpirationMs": getattr(dataset, "default_table_expiration_ms", None),
            "tablePreflight": tables, "transport": "local-file-upload", "sandboxCompatible": True,
            "billingAccountInspected": False}


def _job_details(job):
    return {"jobId": job.job_id, "state": job.state, "errors": getattr(job, "errors", None),
            "projectId": getattr(job, "project", None), "location": getattr(job, "location", None),
            "createdAt": job.created.isoformat() if getattr(job, "created", None) else None,
            "startedAt": job.started.isoformat() if getattr(job, "started", None) else None,
            "completedAt": job.ended.isoformat() if getattr(job, "ended", None) else None,
            "totalBytesProcessed": getattr(job, "total_bytes_processed", None),
            "totalBytesBilled": getattr(job, "total_bytes_billed", None)}


def load_native(client, path, table_id, file_format, config, run_id="manual", schema=None, api=None, include_job_evidence=False):
    """Load one complete file safely; retries recover the identical content-addressed job.

    CSV/JSONL infer schemas unless supplied. Avro/ORC/Parquet use file schemas.
    A different file never overwrites an existing non-empty table.
    """
    if api is None:
        from google.cloud import bigquery as api
    gcp = validate_config(config)
    if file_format not in FORMATS:
        raise ValueError(f"Unsupported native file format {file_format}; Delta/Iceberg require separate table adapters")
    expected_prefix = f"{gcp['projectId']}.{gcp['dataset']}."
    if not table_id.startswith(expected_prefix) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,1023}", table_id[len(expected_prefix):]):
        raise ValueError("Load destination must be a table in the configured dataset")
    path = Path(path)
    if path.stat().st_size > config.get("maximumLocalFileBytes", 100_000_000):
        raise ValueError("Local file exceeds maximumLocalFileBytes; use a smaller learning dataset")
    digest = sha256(path)
    identity = json.dumps({"table": table_id, "sha256": digest, "format": file_format,
                           "schema": schema, "write": "WRITE_EMPTY", "location": gcp["location"]}, sort_keys=True)
    job_id = "forge_load_" + hashlib.sha256(identity.encode("utf-8")).hexdigest()[:48]
    job_config = api.LoadJobConfig()
    job_config.source_format = FORMATS[file_format]
    job_config.write_disposition = "WRITE_EMPTY"
    job_config.create_disposition = "CREATE_IF_NEEDED"
    job_config.max_bad_records = 0
    job_config.ignore_unknown_values = False
    job_config.labels = {"application": "contoso-forge", "forge-run": hashlib.sha256(run_id.encode()).hexdigest()[:32]}
    if schema:
        job_config.schema = [api.SchemaField.from_api_repr(field) for field in schema]
    elif file_format in ("csv", "jsonl"):
        job_config.autodetect = True
    if file_format == "csv":
        job_config.skip_leading_rows = 1
        job_config.allow_quoted_newlines = True
        job_config.encoding = "UTF-8"
    try:
        job = client.get_job(job_id, project=gcp["projectId"], location=gcp["location"])
    except Exception as error:
        if _status_code(error) != 404:
            raise
        try:
            with path.open("rb") as source:
                job = client.load_table_from_file(source, table_id, job_id=job_id,
                                                  job_config=job_config, location=gcp["location"], rewind=True)
        except Exception as submit_error:
            # Duplicate submission or an ambiguous transport failure: recover only
            # the same deterministic job. A missing job still fails visibly.
            if _status_code(submit_error) not in (409, 500, 502, 503, 504):
                raise
            job = client.get_job(job_id, project=gcp["projectId"], location=gcp["location"])
    job.result(timeout=600)
    if job.errors or job.state != "DONE" or job.output_rows is None:
        raise RuntimeError(f"Native load did not complete successfully: {job_id}: {job.errors}")
    result = {"jobId": job.job_id, "tableId": table_id, "state": job.state,
              "inputSha256": digest, "sourceFormat": FORMATS[file_format], "outputRows": int(job.output_rows)}
    if include_job_evidence:
        result.update(_job_details(job))
    return result


def query_measured(client, sql, config, api=None, evidence=None):
    if api is None:
        from google.cloud import bigquery as api
    gcp = validate_config(config)
    job_config = api.QueryJobConfig()
    job_config.use_legacy_sql = False
    job_config.maximum_bytes_billed = gcp["maximumBytesBilled"]
    job_config.labels = {"application": "contoso-forge", "purpose": "truth-reconciliation"}
    job = client.query(sql, job_config=job_config, location=gcp["location"])
    rows = list(job.result(timeout=600))
    if job.errors or job.state != "DONE":
        raise RuntimeError(f"BigQuery reconciliation query failed: {job.errors}")
    if evidence is not None:
        evidence.update(_job_details(job))
    return rows, job.job_id


def execute(root, silver_root, work_order, result_path, client=None, api=None, runtime_path=None):
    root, silver_root = Path(root).resolve(), Path(silver_root).resolve()
    order = read_json(work_order)
    truth, hashes, source_counts = validate_order(root, order)
    config = read_json(root / "gcp/bigquery_config.json")
    gcp = validate_config(config)
    if config.get("warehouse") != "bigquery":
        raise ValueError("Select the BigQuery execution adapter to run these jobs")
    version = order["contractVersion"]
    runtime = None
    if version == "1.3":
        if order["executionScope"] != "spark-and-bigquery":
            raise ValueError("This work order is Spark-only; issue a new BigQuery work order before cloud execution")
        runtime = read_json(runtime_path or root / "colab/spark_runtime.json")
        validate_runtime(order, runtime, truth)
    execution_origin = "google-bigquery-api" if client is None else "injected-client"
    if client is None:
        from google.cloud import bigquery
        try:
            client = bigquery.Client(project=gcp["projectId"], location=gcp["location"])
        except Exception as error:
            if type(error).__name__ in ("DefaultCredentialsError", "RefreshError"):
                raise RuntimeError("BigQuery credentials are missing or expired. Run Colab auth.authenticate_user() or refresh local Application Default Credentials.") from None
            raise
    started = runtime["startedAt"] if runtime else utcnow().isoformat()
    silver_counts, warehouse_counts, jobs, query_jobs = {}, {}, {}, {}
    query_details = {}
    prefix = table_prefix(order)
    dataset = f"{gcp['projectId']}.{gcp['dataset']}"
    # V1.2 injected clients retain their test interface. Actual clients always
    # preflight; V1.3 clients must supply explicit dataset/table observations.
    checked = preflight(client, config, [dataset + "." + prefix + table for table in sorted(truth["expectedSilverRowCounts"])]) if version == "1.3" or execution_origin == "google-bigquery-api" else None
    table_files = {}
    # Verify the whole upload set before submitting the first cloud job.
    for table in sorted(truth["expectedSilverRowCounts"]):
        if not re.fullmatch(r"[a-z][a-z0-9_]*", table):
            raise ValueError(f"Invalid Silver table identifier: {table}")
        files = sorted((silver_root / table).glob("*.parquet"))
        if len(files) != 1:
            raise ValueError(f"Expected exactly one Parquet file for {table}; run the generated Spark coalesce(1) pipeline")
        if files[0].stat().st_size > config.get("maximumLocalFileBytes", 100_000_000):
            raise ValueError("Local file exceeds maximumLocalFileBytes; use a smaller learning dataset")
        if runtime:
            name = files[0].relative_to(silver_root).as_posix()
            if runtime["silverFileSha256"].get(name) != sha256(files[0]):
                raise ValueError(f"Silver file checksum differs from measured Spark output: {table}")
        table_files[table] = files[0]
    # V1 coalesce(1) writes one complete Parquet file per table. Do not append
    # shards independently: a retried append can duplicate data.
    for table in sorted(truth["expectedSilverRowCounts"]):
        import pyarrow.parquet as pq
        # Read observed file metadata, never assign expected counts to results.
        silver_counts[table] = pq.ParquetFile(table_files[table]).metadata.num_rows
        table_id = dataset + "." + prefix + table
        jobs[table] = load_native(client, table_files[table], table_id, "parquet", config, order["runId"], api=api, include_job_evidence=version == "1.3")
        query_details[table] = {}
        rows, query_jobs[table] = query_measured(client, f"SELECT COUNT(*) AS row_count FROM `{table_id}`", config, api, query_details[table])
        if len(rows) != 1:
            raise ValueError(f"Missing COUNT result for {table}")
        warehouse_counts[table] = int(rows[0]["row_count"])
    sql = (root / "gcp/reconcile_kpis.sql").read_text(encoding="utf-8")
    sql = sql.replace("{{dataset}}", dataset).replace("{{prefix}}", prefix)
    query_details["kpis"] = {}
    rows, query_jobs["kpis"] = query_measured(client, sql, config, api, query_details["kpis"])
    if len(rows) != 1:
        raise ValueError("Expected one business KPI result row")
    kpis = {key: str(rows[0][key]) for key in truth["expectedKpis"]}
    result = {
        "contractVersion": version, "status": "completed", "executionRuntime": runtime["executionRuntime"] if runtime else "google-colab-interactive",
        "workOrderId": order["workOrderId"], "runId": order["runId"],
        "datasetFingerprint": order["datasetFingerprint"], "truthManifestSha256": order["truthManifestSha256"],
        "startedAt": started, "completedAt": utcnow().isoformat(),
        "sourceFileSha256": hashes, "sourceRowCounts": source_counts, "silverRowCounts": silver_counts,
        "warehouseRowCounts": warehouse_counts, "kpis": kpis, "loadJobs": jobs, "queryJobs": query_jobs,
        "warehouse": {"provider": "bigquery", "projectId": gcp["projectId"], "dataset": gcp["dataset"], "location": gcp["location"]}
    }
    if runtime:
        result.update(resultScope="spark-and-bigquery", runtimeEvidence=runtime,
                      packageFileSha256=order["packageFileSha256"], truthReconciled=True,
                      bigQueryEvidence={"executionOrigin": execution_origin, "preflight": checked,
                                        "maximumBytesBilled": gcp["maximumBytesBilled"], "queryJobs": query_details})
    # Always save actual observations, including failed comparisons, for diagnosis.
    # A failing reconciliation never emits a successful process exit status.
    write_json(result_path, result)
    try:
        reconcile(root, order, result)
    except Exception:
        if runtime:
            result["truthReconciled"] = False
            write_json(result_path, result)
        raise
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    load = sub.add_parser("load", help="Load one CSV/JSONL/Avro/ORC/Parquet file using WRITE_EMPTY")
    load.add_argument("--config", default="gcp/bigquery_config.json")
    load.add_argument("--file", required=True)
    load.add_argument("--table", required=True, help="Fully qualified project.dataset.table")
    load.add_argument("--format", choices=tuple(FORMATS), required=True)
    load.add_argument("--schema", help="Optional BigQuery JSON field-schema array for CSV/JSONL")
    load.add_argument("--run-id", default="manual")
    run = sub.add_parser("run")
    run.add_argument("--root", default=".")
    run.add_argument("--silver-root", default="lake/silver")
    run.add_argument("--work-order", default="colab/work_order.json")
    run.add_argument("--result", default="colab/result_manifest.json")
    run.add_argument("--runtime", default=None, help="V1.3 measured Spark runtime evidence; defaults to <root>/colab/spark_runtime.json")
    check = sub.add_parser("preflight", help="Check authentication and native dataset location before loading")
    check.add_argument("--config", default="gcp/bigquery_config.json")
    check.add_argument("--create-dataset", action="store_true", help="Explicitly create a missing dataset with Sandbox-compatible 60-day table expiration")
    args = parser.parse_args()
    if args.command == "load":
        from google.cloud import bigquery
        config = read_json(args.config)
        gcp = validate_config(config)
        client = bigquery.Client(project=gcp["projectId"], location=gcp["location"])
        preflight(client, config, [args.table])
        evidence = load_native(client, args.file, args.table, args.format, config,
                               args.run_id, read_json(args.schema) if args.schema else None)
        print(json.dumps(evidence, sort_keys=True))
    elif args.command == "preflight":
        from google.cloud import bigquery
        config = read_json(args.config)
        gcp = validate_config(config)
        client = bigquery.Client(project=gcp["projectId"], location=gcp["location"])
        print(json.dumps(preflight(client, config, create_dataset=args.create_dataset), sort_keys=True))
    else:
        result = execute(args.root, args.silver_root, args.work_order, args.result, runtime_path=args.runtime)
        print(f"BigQuery rows and KPIs reconciled for work order {result['workOrderId']}")


if __name__ == "__main__":
    main()
