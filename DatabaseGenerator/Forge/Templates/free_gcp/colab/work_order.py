#!/usr/bin/env python3
"""Interactive handoff packaging and strict, offline result reconciliation.

This contract verifies measured results against the original local truth. It is
not a cryptographic attestation from Colab; do not accept untrusted operators.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import uuid
import zipfile
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from pathlib import Path


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"), object_pairs_hook=_unique_object,
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError(f"Invalid number: {value}")))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def timestamp(value):
    if not isinstance(value, str):
        raise ValueError("A UTC timestamp is required")
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("Timestamp must include a timezone")
    return parsed.astimezone(timezone.utc)


def utcnow():
    return datetime.now(timezone.utc)


def hash_mapping(hashes):
    if not isinstance(hashes, dict) or not hashes:
        raise ValueError("A non-empty file checksum map is required")
    for name, digest in hashes.items():
        if not isinstance(name, str) or not name or not re.fullmatch(r"[a-f0-9]{64}", str(digest)):
            raise ValueError("Invalid file checksum map")
    return hashlib.sha256("\n".join(f"{name}:{hashes[name]}" for name in sorted(hashes)).encode()).hexdigest()


def safe_path(root, relative):
    if not isinstance(relative, str) or not relative or "\\" in relative:
        raise ValueError("Invalid package path")
    root = Path(root).resolve()
    path = (root / relative).resolve()
    if not path.is_relative_to(root) or path == root:
        raise ValueError(f"Path escapes project: {relative}")
    return path


def verify_sources(root):
    root = Path(root).resolve()
    truth = read_json(root / "truth_manifest.json")
    hashes = truth["sourceFileSha256"]
    if not hashes:
        raise ValueError("Truth manifest contains no source files")
    observed, counts = {}, {}
    for name in sorted(hashes):
        if Path(name).name != name or not name.endswith(".csv"):
            raise ValueError(f"Unsupported source entry: {name}")
        path = safe_path(root, "data/source/" + name)
        observed[name] = sha256(path)
        if observed[name] != hashes[name]:
            raise ValueError(f"Source checksum mismatch: {name}")
        with path.open(newline="", encoding="utf-8-sig") as handle:
            reader = csv.reader(handle, strict=True)
            header = next(reader)
            count = 0
            for row in reader:
                if len(row) != len(header):
                    raise ValueError(f"CSV shape mismatch: {name}")
                count += 1
            counts[Path(name).stem] = count
    canonical = "\n".join(f"{name}:{observed[name]}" for name in sorted(observed))
    fingerprint = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    if fingerprint != truth["datasetFingerprint"]:
        raise ValueError("Dataset fingerprint does not match source bytes")
    if counts != truth["sourceRowCounts"]:
        raise ValueError("Observed source row counts do not match truth")
    return truth, observed, counts


def validate_order(root, order, now=None, allow_completed_expired=False):
    now = now or utcnow()
    if order.get("contractVersion") not in ("1.2", "1.3") or order.get("status") != "issued":
        raise ValueError("Work order is not an issued 1.2/1.3 contract")
    for field in ("workOrderId", "runId", "datasetFingerprint"):
        if not isinstance(order.get(field), str) or not order[field]:
            raise ValueError(f"Work order missing {field}")
    issued, expires = timestamp(order["issuedAt"]), timestamp(order["expiresAt"])
    if issued > now or expires <= issued or (now > expires and not allow_completed_expired):
        raise ValueError("Work order is future-dated or expired")
    truth, hashes, counts = verify_sources(root)
    if order["datasetFingerprint"] != truth["datasetFingerprint"]:
        raise ValueError("Work order dataset fingerprint mismatch")
    if order.get("sourceFileSha256") != hashes or order.get("truthManifestSha256") != sha256(Path(root) / "truth_manifest.json"):
        raise ValueError("Work order does not match the original source/truth manifest")
    package_files = order.get("packageFileSha256", {})
    if not package_files or "gcp/bigquery_config.json" not in package_files or "pyspark/bronze_silver.py" not in package_files:
        raise ValueError("Work order is missing its execution package hashes")
    for name, digest in package_files.items():
        if sha256(safe_path(root, name)) != digest:
            raise ValueError(f"Execution package checksum mismatch: {name}")
    config = read_json(Path(root) / "gcp/bigquery_config.json")
    if order.get("warehouse") != config.get("warehouse") or order.get("gcp") != config.get("gcp"):
        raise ValueError("Work order destination mismatch")
    if order["contractVersion"] == "1.3":
        spark_config = read_json(Path(root) / "colab/spark_config.json")
        if "colab/spark_config.json" not in package_files:
            raise ValueError("Work order is missing its Spark configuration hash")
        if order.get("executionScope") not in ("spark", "spark-and-bigquery"):
            raise ValueError("Work order has an invalid execution scope")
        if order.get("requestedSparkApiMode") not in ("classic", "connect-local", "connect-remote"):
            raise ValueError("Work order has an invalid requested Spark mode")
        for order_key, config_key in (("requestedSparkApiMode", "sparkApiMode"),
                                      ("sparkVersionPolicy", "sparkVersionPolicy"),
                                      ("requestedSparkVersion", "sparkVersion")):
            if order.get(order_key) != spark_config.get(config_key):
                raise ValueError(f"Work order {order_key} does not match hashed Spark configuration")
    return truth, hashes, counts


def package(root, run_id, work_order=None, package_path=None, lifetime_hours=24, now=None, scope="spark-and-bigquery"):
    root = Path(root).resolve()
    if not isinstance(run_id, str) or not run_id.strip() or len(run_id) > 250:
        raise ValueError("A non-empty run ID of at most 250 characters is required")
    if lifetime_hours < 1 or lifetime_hours > 168:
        raise ValueError("Work order lifetime must be 1..168 hours")
    now = now or utcnow()
    truth, hashes, _ = verify_sources(root)
    config = read_json(root / "gcp/bigquery_config.json")
    if config.get("warehouse") != "bigquery":
        raise ValueError("This interactive execution adapter supports BigQuery. Select another adapter for this warehouse.")
    if scope not in ("spark", "spark-and-bigquery"):
        raise ValueError("Execution scope must be spark or spark-and-bigquery")
    spark_config_path = root / "colab/spark_config.json"
    spark_config = read_json(spark_config_path) if spark_config_path.is_file() else None
    if scope == "spark" and spark_config is None:
        raise ValueError("Spark-only packages require the V1.3 Spark configuration")
    if scope != "spark" and config.get("gcp", {}).get("projectId") in (None, "", "your-gcp-project"):
        raise ValueError("Set your actual GCP project ID and regenerate before packaging")
    pipeline = "pipeline.json" if (root / "pipeline.json").is_file() else "pipeline/pipeline.json"
    names = ["truth_manifest.json", "resolved_project.json", pipeline, "pyspark/bronze_silver.py",
             "gcp/bigquery_config.json", "gcp/bigquery_runtime.py", "gcp/reconcile_kpis.sql",
             "gcp/requirements.txt", "colab/work_order.py", "colab/run_spark.py"]
    if spark_config is not None:
        names += ["colab/spark_config.json", "colab/spark_session.py", "colab/storage_adapter.py", "colab/bootstrap_runtime.py"]
    # Optional after-Gold adapters travel as authored inputs, never previous dbt/ML outputs.
    # They are not executed by packaging or the default notebook.
    for directory in ("dbt_bigquery", "bqml"):
        folder = root / directory
        if folder.is_dir():
            for path in sorted(folder.rglob("*")):
                relative = path.relative_to(root)
                if path.is_file() and path.suffix in (".sql", ".yml", ".py", ".md", ".txt") and not {
                    "target", "logs", "dbt_packages", "__pycache__"
                }.intersection(relative.parts) and len(relative.parts) <= 4:
                    # Model result directories are named forge_* and contain runtime outputs.
                    if directory == "bqml" and len(relative.parts) != 2:
                        continue
                    names.append(relative.as_posix())
    names += ["data/source/" + name for name in sorted(hashes)]
    file_hashes = {name: sha256(safe_path(root, name)) for name in sorted(names)}
    order_path = Path(work_order) if work_order else root / "colab/work_order.json"
    zip_path = Path(package_path) if package_path else root / "colab/work_package.zip"
    # A scheduler retry must preserve identity. A new run uses a new state directory.
    if order_path.exists():
        order = read_json(order_path)
        if order.get("runId") != run_id:
            raise ValueError("Work order path already belongs to another run; use a unique state directory")
        validate_order(root, order, now)
        if order.get("executionScope", "spark-and-bigquery") != scope:
            raise ValueError("Existing work order has a different execution scope")
        if order.get("packageFileSha256") != file_hashes:
            raise ValueError("Existing work order refers to different runtime artifacts")
    else:
        order = {
            "contractVersion": "1.3" if spark_config is not None else "1.2", "artifactStatus": "experimental", "status": "issued",
            "workOrderId": str(uuid.uuid4()), "runId": run_id,
            "issuedAt": now.isoformat(), "expiresAt": (now + timedelta(hours=lifetime_hours)).isoformat(),
            "datasetFingerprint": truth["datasetFingerprint"], "sourceFileSha256": hashes,
            "truthManifestSha256": sha256(root / "truth_manifest.json"), "packageFileSha256": file_hashes,
            "warehouse": config["warehouse"], "gcp": config["gcp"],
            "resultPath": "colab/result_manifest.json"
        }
        if spark_config is not None:
            order.update(executionScope=scope, requestedSparkApiMode=spark_config["sparkApiMode"],
                         sparkVersionPolicy=spark_config["sparkVersionPolicy"], requestedSparkVersion=spark_config["sparkVersion"])
        write_json(order_path, order)
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(names):
            archive.write(safe_path(root, name), name)
        archive.writestr("colab/work_order.json", json.dumps(order, sort_keys=True, indent=2) + "\n")
    return order


def _counts(actual, expected, name):
    if not isinstance(actual, dict) or set(actual) != set(expected):
        raise ValueError(f"{name} missing/unexpected tables")
    for table, count in expected.items():
        if type(actual[table]) is not int or actual[table] != count:
            raise ValueError(f"{name} mismatch: {table}, expected {count}, observed {actual[table]}")


def validate_runtime(order, runtime, truth, now=None):
    """Validate measured API mode and its binding to one issued execution package."""
    now = now or utcnow()
    if not isinstance(runtime, dict) or runtime.get("contractVersion") != "1.3" or runtime.get("status") != "succeeded":
        raise ValueError("Successful V1.3 Spark runtime evidence is required")
    for field in ("workOrderId", "runId", "datasetFingerprint", "truthManifestSha256", "packageFileSha256", "sourceFileSha256"):
        if runtime.get(field) != order.get(field):
            raise ValueError(f"Spark runtime {field} mismatch")
    if runtime.get("executionRuntime") not in ("google-colab-interactive", "local-python"):
        raise ValueError("Spark runtime execution environment is required")
    started, finished = timestamp(runtime.get("startedAt")), timestamp(runtime.get("completedAt"))
    if started < timestamp(order["issuedAt"]) or finished < started or finished > now or finished > timestamp(order["expiresAt"]):
        raise ValueError("Spark runtime timestamps are outside the issued run")
    requested, actual = runtime.get("requestedSparkApiMode"), runtime.get("actualSparkApiMode")
    if requested != order["requestedSparkApiMode"] or actual not in ("classic", "connect-local", "connect-remote"):
        raise ValueError("Spark runtime requested/actual mode mismatch")
    for field in ("sparkVersionPolicy", "requestedSparkVersion"):
        if runtime.get(field) != order[field]:
            raise ValueError(f"Spark runtime {field} does not match the issued configuration")
    reason = runtime.get("fallbackReason")
    if requested != actual:
        if actual != "classic" or not isinstance(reason, str) or not reason.strip():
            raise ValueError("Spark mode fallback must be explicit and include fallbackReason")
    elif reason not in (None, ""):
        raise ValueError("Spark fallbackReason cannot claim a fallback when modes match")
    session_class = runtime.get("sparkSessionClass", "")
    connect = actual.startswith("connect-")
    if type(runtime.get("isRemote")) is not bool or runtime["isRemote"] != connect:
        raise ValueError("Spark isRemote does not prove the declared actual API mode")
    if not isinstance(session_class, str) or not session_class.endswith("SparkSession") or (".connect." in session_class) != connect:
        raise ValueError("Spark session class does not match the actual API mode")
    master = runtime.get("masterOrRemote")
    if not isinstance(master, str) or not master:
        raise ValueError("Spark masterOrRemote is required")
    if actual == "classic" and not re.fullmatch(r"local(?:\[(?:\*|[1-9][0-9]*)(?:,[1-9][0-9]*)?\])?", master):
        raise ValueError("Classic Colab evidence requires a local master")
    if actual == "connect-local" and not (master.startswith("local") or re.match(r"sc://(?:localhost|127\.0\.0\.1)(?::[0-9]+)?(?:/|$)", master)):
        raise ValueError("Connect-local evidence requires a local Connect endpoint")
    if actual == "connect-remote" and (not master.startswith("sc://") or re.match(r"sc://(?:localhost|127\.0\.0\.1)(?::|/|$)", master)):
        raise ValueError("Connect-remote evidence requires a distinct remote endpoint")
    for field in ("pythonVersion", "javaVersion", "pysparkVersion", "sparkVersion", "inputTransport"):
        if not isinstance(runtime.get(field), str) or not runtime[field].strip():
            raise ValueError(f"Spark runtime is missing exact {field}")
    if connect and not re.match(r"[4-9][0-9]*\.", runtime["sparkVersion"]):
        raise ValueError("This Connect profile requires Spark 4.x or newer")
    if order["sparkVersionPolicy"] == "pinned" and any(runtime[field] != order["requestedSparkVersion"] for field in ("pysparkVersion", "sparkVersion")):
        raise ValueError("Spark runtime does not match the pinned version")
    if type(runtime.get("cpuCount")) is not int or runtime["cpuCount"] < 1 or not isinstance(runtime.get("memorySummary"), dict) or not runtime["memorySummary"]:
        raise ValueError("Spark CPU/memory evidence is required")
    if runtime.get("inputFingerprint") != order["datasetFingerprint"] or runtime.get("truthReconciled") is not True:
        raise ValueError("Spark input fingerprint/truth reconciliation failed")
    _counts(runtime.get("sourceRowCounts"), truth["sourceRowCounts"], "Spark source row count")
    _counts(runtime.get("bronzeRowCounts"), truth["sourceRowCounts"], "Bronze row count")
    _counts(runtime.get("silverRowCounts"), truth["expectedSilverRowCounts"], "Spark Silver row count")
    for layer, expected in (("bronze", truth["sourceRowCounts"]), ("silver", truth["expectedSilverRowCounts"])):
        hashes = runtime.get(layer + "FileSha256")
        if runtime.get(layer + "Fingerprint") != hash_mapping(hashes):
            raise ValueError(f"Spark {layer} fingerprint does not match its file hashes")
        tables = set()
        for name in hashes:
            parts = name.split("/")
            if len(parts) < 2 or any(part in ("", ".", "..") for part in parts) or "\\" in name or ":" in name or not name.endswith(".parquet"):
                raise ValueError(f"Invalid {layer} Parquet evidence path")
            tables.add(parts[0])
        if tables != set(expected):
            raise ValueError(f"Spark {layer} file evidence missing/unexpected tables")
    if connect:
        smoke = runtime.get("dataframeSmoke", {})
        if not isinstance(smoke, dict) or any(smoke.get(key) is not True for key in ("dataframe", "window", "dedup", "parquetRoundTrip")):
            raise ValueError("Connect requires successful DataFrame/Window/dedup/Parquet smoke evidence")
    return runtime


def spark_result(root, order, runtime, now=None):
    """Return the generated Forge Spark gate independently of BigQuery authentication."""
    truth, _, _ = validate_order(root, order, now)
    if order["contractVersion"] != "1.3":
        raise ValueError("Spark-only results require a V1.3 work order")
    validate_runtime(order, runtime, truth, now)
    fields = ("workOrderId", "runId", "datasetFingerprint", "truthManifestSha256", "packageFileSha256",
              "sourceFileSha256", "sourceRowCounts", "silverRowCounts", "startedAt", "completedAt", "executionRuntime")
    result = {field: runtime[field] for field in fields}
    result.update(contractVersion="1.3", status="completed", resultScope="spark", truthReconciled=True,
                  runtimeEvidence=runtime)
    return result


def reconcile(root, order, result, now=None, allow_completed_expired=False, allow_partial_spark=False):
    now = now or utcnow()
    truth, hashes, counts = validate_order(root, order, now, allow_completed_expired)
    version = order["contractVersion"]
    if result.get("contractVersion") != version or result.get("status") != "completed":
        raise ValueError(f"A completed {version} result manifest is required")
    if result.get("executionRuntime") not in (("google-colab-interactive", "local-python") if version == "1.3" else ("google-colab-interactive",)):
        raise ValueError("Result execution runtime does not match the Colab adapter")
    for field in ("workOrderId", "runId", "datasetFingerprint", "truthManifestSha256"):
        if result.get(field) != order[field]:
            raise ValueError(f"Result {field} mismatch")
    started, finished = timestamp(result["startedAt"]), timestamp(result["completedAt"])
    if started < timestamp(order["issuedAt"]) or finished < started or finished > now or finished > timestamp(order["expiresAt"]):
        raise ValueError("Result timestamps are stale, out of order, or in the future")
    if result.get("sourceFileSha256") != hashes:
        raise ValueError("Result source checksums mismatch")
    _counts(result.get("sourceRowCounts"), counts, "Source row count")
    _counts(result.get("silverRowCounts"), truth["expectedSilverRowCounts"], "Silver row count")
    report = {"contractVersion": version, "status": "reconciled", "workOrderId": order["workOrderId"],
              "runId": order["runId"], "datasetFingerprint": order["datasetFingerprint"]}
    if version == "1.3":
        runtime = validate_runtime(order, result.get("runtimeEvidence"), truth, now)
        if result.get("packageFileSha256") != order["packageFileSha256"]:
            raise ValueError("Result execution package hashes mismatch")
        if result.get("executionRuntime") != runtime["executionRuntime"]:
            raise ValueError("Result execution runtime mismatch with measured Spark environment")
        if started > timestamp(runtime["startedAt"]) or finished < timestamp(runtime["completedAt"]):
            raise ValueError("Result timestamps do not enclose the Spark execution")
        if result.get("truthReconciled") is not True:
            raise ValueError("Result truthReconciled must be true")
        if result.get("resultScope") == "spark":
            if order["executionScope"] != "spark" and not allow_partial_spark:
                raise ValueError("Spark-only evidence cannot complete a spark-and-bigquery work order")
            if any(result.get(field) for field in ("warehouseRowCounts", "kpis", "loadJobs", "queryJobs", "warehouse")):
                raise ValueError("Spark-only results cannot claim warehouse execution")
            return dict(report, resultScope="spark")
        if result.get("resultScope") != "spark-and-bigquery" or order["executionScope"] != "spark-and-bigquery":
            raise ValueError("Result scope does not match the issued BigQuery execution scope")
    _counts(result.get("warehouseRowCounts"), truth["expectedSilverRowCounts"], "Warehouse row count")
    kpis = result.get("kpis")
    if not isinstance(kpis, dict) or set(kpis) != set(truth["expectedKpis"]):
        raise ValueError("Result missing/unexpected KPIs")
    for key, expected in truth["expectedKpis"].items():
        value = kpis[key]
        if isinstance(value, bool) or not isinstance(value, (str, int, float)):
            raise ValueError(f"Invalid KPI value: {key}")
        actual = Decimal(str(value))
        if not actual.is_finite() or abs(actual - Decimal(str(expected))) > Decimal("0.000001"):
            raise ValueError(f"KPI mismatch: {key}, expected {expected}, observed {value}")
    warehouse = result.get("warehouse", {})
    if warehouse.get("provider") != "bigquery" or warehouse.get("projectId") != order["gcp"]["projectId"] or warehouse.get("dataset") != order["gcp"]["dataset"] or warehouse.get("location") != order["gcp"]["location"]:
        raise ValueError("Result warehouse destination mismatch")
    jobs = result.get("loadJobs", {})
    if set(jobs) != set(truth["expectedSilverRowCounts"]):
        raise ValueError("Result is missing native load job evidence")
    prefix = table_prefix(order)
    for table, job in jobs.items():
        expected_table = f"{order['gcp']['projectId']}.{order['gcp']['dataset']}.{prefix}{table}"
        if not isinstance(job, dict) or job.get("tableId") != expected_table or job.get("state") != "DONE" or not re.fullmatch(r"forge_load_[a-f0-9]{48}", str(job.get("jobId", ""))):
            raise ValueError(f"Invalid load job evidence for {table}")
        if type(job.get("outputRows")) is not int or job["outputRows"] != truth["expectedSilverRowCounts"][table] or not re.fullmatch(r"[a-f0-9]{64}", str(job.get("inputSha256", ""))) or job.get("sourceFormat") != "PARQUET":
            raise ValueError(f"Invalid native load row count/checksum for {table}")
    queries = result.get("queryJobs", {})
    if set(queries) != set(truth["expectedSilverRowCounts"]) | {"kpis"} or any(not isinstance(value, str) or not value for value in queries.values()):
        raise ValueError("Result is missing actual query job evidence")
    if version == "1.3":
        cloud = result.get("bigQueryEvidence", {})
        if cloud.get("executionOrigin") not in ("google-bigquery-api", "injected-client"):
            raise ValueError("BigQuery execution origin is required")
        preflight = cloud.get("preflight", {})
        if preflight.get("status") != "ready" or preflight.get("datasetId") != f"{order['gcp']['projectId']}.{order['gcp']['dataset']}" or preflight.get("location", "").upper() != order["gcp"]["location"].upper():
            raise ValueError("BigQuery dataset preflight evidence mismatch")
        if cloud.get("maximumBytesBilled") != order["gcp"]["maximumBytesBilled"]:
            raise ValueError("BigQuery cost guard evidence mismatch")
        details = cloud.get("queryJobs", {})
        if set(details) != set(queries):
            raise ValueError("BigQuery query completion evidence is incomplete")
        for key, detail in details.items():
            if not isinstance(detail, dict) or detail.get("jobId") != queries[key] or detail.get("state") != "DONE" or detail.get("errors"):
                raise ValueError(f"BigQuery query did not complete successfully: {key}")
            if detail.get("projectId") != order["gcp"]["projectId"] or str(detail.get("location", "")).upper() != order["gcp"]["location"].upper():
                raise ValueError(f"BigQuery query destination mismatch: {key}")
            for field in ("totalBytesProcessed", "totalBytesBilled"):
                if type(detail.get(field)) is not int or detail[field] < 0:
                    raise ValueError(f"BigQuery query missing measured {field}: {key}")
            if detail["totalBytesBilled"] > cloud["maximumBytesBilled"]:
                raise ValueError("BigQuery query exceeded maximumBytesBilled")
            _job_times(detail, order, result)
        for table, job in jobs.items():
            actual_files = [digest for name, digest in runtime["silverFileSha256"].items() if name.split("/")[0] == table]
            if actual_files != [job["inputSha256"]] or job.get("errors") or job.get("projectId") != order["gcp"]["projectId"] or str(job.get("location", "")).upper() != order["gcp"]["location"].upper():
                raise ValueError(f"BigQuery load does not match measured Silver/runtime: {table}")
            _job_times(job, order, result)
        report["resultScope"] = "spark-and-bigquery"
    return report


def _job_times(job, order, result):
    created = timestamp(job.get("createdAt"))
    started = timestamp(job.get("startedAt"))
    finished = timestamp(job.get("completedAt"))
    if created < timestamp(order["issuedAt"]) or not created <= started <= finished <= timestamp(result["completedAt"]):
        raise ValueError("BigQuery job timestamps are outside the issued result")


def import_evidence(root, work_order, result_path, output, now=None):
    """Import operator-returned observations; this is not a cloud attestation service.

    The source order/result remain immutable. Late imports are allowed only when
    the measured execution completed within the issued work-order window.
    """
    now = now or utcnow()
    order, result = read_json(work_order), read_json(result_path)
    protected = {Path(work_order).resolve(), Path(result_path).resolve()}
    protected.update(safe_path(root, name) for name in order.get("packageFileSha256", {}))
    if Path(output).resolve() in protected:
        raise ValueError("Evidence output must be separate from issued order and returned result")
    report = reconcile(root, order, result, now, allow_completed_expired=True, allow_partial_spark=True)
    if Path(output).exists():
        previous = read_json(output)
        if previous.get("workOrderId") != order["workOrderId"] or previous.get("runId") != order["runId"]:
            raise ValueError("Evidence output already belongs to another issued run")
    statuses = {"colab-spark-classic": "pending", "colab-spark-connect-local": "pending",
                "spark-connect-remote": "pending", "bigquery-sandbox": "pending", "minikube-airflow": "pending"}
    runtime = result.get("runtimeEvidence", {})
    hosted = order["contractVersion"] == "1.3" and runtime.get("executionRuntime") == "google-colab-interactive"
    if order["contractVersion"] == "1.3":
        mode_key = {"classic": "colab-spark-classic", "connect-local": "colab-spark-connect-local",
                    "connect-remote": "spark-connect-remote"}[runtime["actualSparkApiMode"]]
        statuses[mode_key] = "validated-user-runtime" if hosted else "validated-local-runtime"
        if result["resultScope"] == "spark-and-bigquery":
            statuses["bigquery-sandbox"] = "validated-user-runtime" if hosted and result["bigQueryEvidence"]["executionOrigin"] == "google-bigquery-api" else "reconciled-non-hosted-evidence"
    report.update(status="evidence-imported", evidenceOrigin="user-returned" if hosted else "local-or-legacy-result",
                  verificationKind="offline-contract-and-truth-reconciliation", importedAt=now.isoformat(),
                  workOrderSha256=sha256(work_order), resultManifestSha256=sha256(result_path),
                  truthManifestSha256=order["truthManifestSha256"], packageFileSha256=order["packageFileSha256"],
                  runtimeEvidenceSha256=hashlib.sha256(json.dumps(runtime, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest() if runtime else None,
                  runtimeStatus=statuses, overallArchitectureStatus="pending-live-gates",
                  legacyRuntimeUnverified=order["contractVersion"] == "1.2")
    write_json(output, report)
    return report


def table_prefix(order):
    identity = order["runId"] + "\n" + order["workOrderId"] + "\n" + order["datasetFingerprint"]
    return "forge_" + hashlib.sha256(identity.encode("utf-8")).hexdigest()[:20] + "_"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    verify = sub.add_parser("verify-source")
    verify.add_argument("--root", default=".")
    prepare = sub.add_parser("package")
    prepare.add_argument("--root", default=".")
    prepare.add_argument("--run-id", required=True)
    prepare.add_argument("--work-order")
    prepare.add_argument("--package")
    prepare.add_argument("--lifetime-hours", type=int, default=24)
    prepare.add_argument("--scope", choices=("spark", "spark-and-bigquery"), default="spark-and-bigquery",
                         help="Spark-only packaging does not require a configured BigQuery project")
    check = sub.add_parser("reconcile")
    check.add_argument("--root", default=".")
    check.add_argument("--work-order", required=True)
    check.add_argument("--result", required=True)
    check.add_argument("--output")
    imported = sub.add_parser("import-evidence", help="Validate a returned result and save separate run-bound evidence")
    imported.add_argument("--root", default=".")
    imported.add_argument("--work-order", required=True)
    imported.add_argument("--result", required=True)
    imported.add_argument("--output", required=True)
    spark = sub.add_parser("spark-result", help="Write a Spark-only result before BigQuery authentication")
    spark.add_argument("--root", default=".")
    spark.add_argument("--work-order", required=True)
    spark.add_argument("--runtime", default="colab/spark_runtime.json")
    spark.add_argument("--output", "--result", dest="output", default="colab/spark_result_manifest.json")
    args = parser.parse_args()
    if args.command == "verify-source":
        truth, _, counts = verify_sources(args.root)
        print(json.dumps({"datasetFingerprint": truth["datasetFingerprint"], "sourceRowCounts": counts}, sort_keys=True))
    elif args.command == "package":
        order = package(args.root, args.run_id, args.work_order, args.package, args.lifetime_hours, scope=args.scope)
        print(json.dumps({"status": "awaiting-manual-colab", "workOrderId": order["workOrderId"], "runId": order["runId"]}))
    elif args.command == "import-evidence":
        print(json.dumps(import_evidence(args.root, args.work_order, args.result, args.output), sort_keys=True))
    elif args.command == "spark-result":
        result = spark_result(args.root, read_json(args.work_order), read_json(args.runtime))
        write_json(args.output, result)
        print(json.dumps({"status": "spark-truth-reconciled", "workOrderId": result["workOrderId"], "result": args.output}))
    else:
        report = reconcile(args.root, read_json(args.work_order), read_json(args.result))
        if args.output:
            write_json(args.output, report)
        print(json.dumps(report, sort_keys=True))


if __name__ == "__main__":
    main()
