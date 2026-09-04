#!/usr/bin/env python3
"""Build run-scoped BigQuery Gold only after a reconciled native load."""
from __future__ import annotations
import argparse
import os
import sys
from decimal import Decimal
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "colab"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "gcp"))
from work_order import read_json, write_json, reconcile, table_prefix, utcnow, sha256
from bigquery_runtime import validate_config, query_measured

EXPECTED_MODELS = {
    'stg_customers', 'stg_customer_cdc', 'stg_customer_scd2', 'stg_products', 'stg_stores', 'stg_orders',
    'stg_order_rows', 'stg_shipments', 'stg_shipment_events', 'stg_returns', 'stg_support_tickets', 'stg_reviews',
    'stg_quality_issues', 'dim_date', 'dim_customer', 'dim_product', 'dim_store', 'dim_carrier',
    'fact_sales', 'fact_shipment', 'fact_return', 'fact_support', 'fact_customer_experience', 'kpi_customer_satisfaction'}


def validate_results(results):
    rows = results.get('results', [])
    if not rows or any(item['status'] not in ('success', 'pass') for item in rows):
        raise ValueError('Every dbt model/test must succeed')
    models = {row['unique_id'].split('.')[-1] for row in rows if row['unique_id'].startswith('model.')}
    if not EXPECTED_MODELS.issubset(models) or not any(row['unique_id'] == 'test.contoso_forge_bigquery.reconcile_truth' for row in rows):
        raise ValueError('Gold requires all 24 models and the truth reconciliation test')


def compare_kpis(rows, expected):
    if len(rows) != 1:
        raise ValueError('Gold must expose exactly one KPI row')
    actual = {key: str(rows[0][key]) for key in expected}
    for key, value in expected.items():
        measured = Decimal(actual[key])
        if not measured.is_finite() or abs(measured - Decimal(str(value))) > Decimal('0.000001'):
            raise ValueError('Measured BigQuery Gold KPI differs from truth: ' + key)
    return actual


def configure(root, order):
    config = read_json(root / "gcp/bigquery_config.json")
    gcp = validate_config(config)
    expected = order.get("gcp", {})
    if any(expected.get(key) != gcp[key] for key in ("projectId", "dataset", "location")):
        raise ValueError("dbt destination must match the issued work order")
    values = {"FORGE_GCP_PROJECT": gcp["projectId"], "FORGE_BQ_DATASET": gcp["dataset"],
              "FORGE_BQ_LOCATION": gcp["location"], "FORGE_BQ_PREFIX": table_prefix(order),
              "FORGE_BQ_MAXIMUM_BYTES_BILLED": str(gcp["maximumBytesBilled"])}
    return values


def run(root, work_order, result, parse_only=False):
    root = Path(root).resolve()
    order = read_json(work_order)
    values = configure(root, order)
    if not parse_only:
        if not result:
            raise ValueError("A reconciled BigQuery result is required before Gold")
        observed = read_json(result)
        reconcile(root, order, observed, allow_completed_expired=True)
        if observed.get("resultScope") != "spark-and-bigquery" or observed.get("bigQueryEvidence", {}).get("executionOrigin") != "google-bigquery-api":
            raise ValueError("BigQuery Gold requires measured native BigQuery load evidence")
    from dbt.cli.main import dbtRunner
    project = root / "dbt_bigquery"
    previous = {key: os.environ.get(key) for key in values}
    started = utcnow().isoformat()
    try:
        os.environ.update(values)
        outcome = dbtRunner().invoke(["parse" if parse_only else "build", "--project-dir", str(project),
                                     "--profiles-dir", str(project), "--no-partial-parse"])
        if not outcome.success:
            raise RuntimeError("dbt failed; inspect dbt_bigquery/logs and target/run_results.json") from outcome.exception
        report = {"contractVersion": "1.3", "status": "parsed" if parse_only else "succeeded",
                  "cloudExecutionVerified": not parse_only, "workOrderId": order["workOrderId"],
                  "runId": order["runId"], "datasetFingerprint": order["datasetFingerprint"],
                  "prefix": values["FORGE_BQ_PREFIX"], "startedAt": started, "completedAt": utcnow().isoformat(),
                  "maximumBytesBilled": int(values["FORGE_BQ_MAXIMUM_BYTES_BILLED"]),
                  "projectId": values["FORGE_GCP_PROJECT"], "dataset": values["FORGE_BQ_DATASET"],
                  "location": values["FORGE_BQ_LOCATION"], "manifestSha256": sha256(project / "target/manifest.json"),
                  "authoredFileSha256": {str(p.relative_to(project)).replace('\\', '/'): sha256(p)
                                         for p in sorted(project.rglob("*"))
                                         if p.is_file() and p.suffix in ('.sql', '.yml')
                                         and not {'target', 'logs', 'dbt_packages'}.intersection(p.relative_to(project).parts)}}
        if not parse_only:
            report["loadResultSha256"] = sha256(result)
            results = read_json(project / "target/run_results.json")
            validate_results(results)
            # Independently measure the Gold relation, even if authored dbt tests were edited.
            from google.cloud import bigquery
            config = read_json(root / 'gcp/bigquery_config.json')
            client = bigquery.Client(project=values['FORGE_GCP_PROJECT'], location=values['FORGE_BQ_LOCATION'])
            evidence = {}
            relation = values['FORGE_GCP_PROJECT'] + '.' + values['FORGE_BQ_DATASET'] + '.' + values['FORGE_BQ_PREFIX'] + 'kpi_customer_satisfaction'
            rows, _ = query_measured(client, 'SELECT * FROM `' + relation + '`', config, bigquery, evidence)
            report['kpis'] = compare_kpis(rows, read_json(root / 'truth_manifest.json')['expectedKpis'])
            report['goldReconciliationJob'] = evidence
            report["runResultsSha256"] = sha256(project / "target/run_results.json")
            report["results"] = [{key: row.get(key) for key in ("unique_id", "status", "execution_time", "adapter_response")}
                                 for row in results["results"]]
        report['completedAt'] = utcnow().isoformat()
        output = project / ("parse_evidence.json" if parse_only else "gold_evidence.json")
        write_json(output, report)
        return report
    finally:
        for key, value in previous.items():
            if value is None: os.environ.pop(key, None)
            else: os.environ[key] = value


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".")
    parser.add_argument("--work-order", default="colab/work_order.json")
    parser.add_argument("--result", default="colab/result_manifest.json")
    parser.add_argument("--parse-only", action="store_true")
    args = parser.parse_args()
    run(args.root, args.work_order, args.result, args.parse_only)
