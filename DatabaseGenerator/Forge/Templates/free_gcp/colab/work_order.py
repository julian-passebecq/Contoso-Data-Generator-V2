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


def validate_order(root, order, now=None):
    now = now or utcnow()
    if order.get("contractVersion") != "1.2" or order.get("status") != "issued":
        raise ValueError("Work order is not an issued 1.2 contract")
    for field in ("workOrderId", "runId", "datasetFingerprint"):
        if not isinstance(order.get(field), str) or not order[field]:
            raise ValueError(f"Work order missing {field}")
    issued, expires = timestamp(order["issuedAt"]), timestamp(order["expiresAt"])
    if issued > now or expires <= issued or now > expires:
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
    return truth, hashes, counts


def package(root, run_id, work_order=None, package_path=None, lifetime_hours=24, now=None):
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
    if config.get("gcp", {}).get("projectId") in (None, "", "your-gcp-project"):
        raise ValueError("Set your actual GCP project ID and regenerate before packaging")
    pipeline = "pipeline.json" if (root / "pipeline.json").is_file() else "pipeline/pipeline.json"
    names = ["truth_manifest.json", "resolved_project.json", pipeline, "pyspark/bronze_silver.py",
             "gcp/bigquery_config.json", "gcp/bigquery_runtime.py", "gcp/reconcile_kpis.sql",
             "gcp/requirements.txt", "colab/work_order.py", "colab/run_spark.py"]
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
        if order.get("packageFileSha256") != file_hashes:
            raise ValueError("Existing work order refers to different runtime artifacts")
    else:
        order = {
            "contractVersion": "1.2", "artifactStatus": "experimental", "status": "issued",
            "workOrderId": str(uuid.uuid4()), "runId": run_id,
            "issuedAt": now.isoformat(), "expiresAt": (now + timedelta(hours=lifetime_hours)).isoformat(),
            "datasetFingerprint": truth["datasetFingerprint"], "sourceFileSha256": hashes,
            "truthManifestSha256": sha256(root / "truth_manifest.json"), "packageFileSha256": file_hashes,
            "warehouse": config["warehouse"], "gcp": config["gcp"],
            "resultPath": "colab/result_manifest.json"
        }
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


def reconcile(root, order, result, now=None):
    now = now or utcnow()
    truth, hashes, counts = validate_order(root, order, now)
    if result.get("contractVersion") != "1.2" or result.get("status") != "completed":
        raise ValueError("A completed 1.2 result manifest is required")
    if result.get("executionRuntime") != "google-colab-interactive":
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
    return {"contractVersion": "1.2", "status": "reconciled", "workOrderId": order["workOrderId"],
            "runId": order["runId"], "datasetFingerprint": order["datasetFingerprint"]}


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
    check = sub.add_parser("reconcile")
    check.add_argument("--root", default=".")
    check.add_argument("--work-order", required=True)
    check.add_argument("--result", required=True)
    check.add_argument("--output")
    args = parser.parse_args()
    if args.command == "verify-source":
        truth, _, counts = verify_sources(args.root)
        print(json.dumps({"datasetFingerprint": truth["datasetFingerprint"], "sourceRowCounts": counts}, sort_keys=True))
    elif args.command == "package":
        order = package(args.root, args.run_id, args.work_order, args.package, args.lifetime_hours)
        print(json.dumps({"status": "awaiting-manual-colab", "workOrderId": order["workOrderId"], "runId": order["runId"]}))
    else:
        report = reconcile(args.root, read_json(args.work_order), read_json(args.result))
        if args.output:
            write_json(args.output, report)
        print(json.dumps(report, sort_keys=True))


if __name__ == "__main__":
    main()
