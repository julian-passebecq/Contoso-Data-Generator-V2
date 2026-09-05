"""Evidence consumes already-built marts and contracts. No KPI expression is executed here."""
import csv
import json
import shutil
from pathlib import Path
import pandas as pd
import duckdb
from common import read, write, sha, literal


def csv_rows(path, rows, columns):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def build(root, state, evidence):
    report = state / "bi/evidence"
    sources = report / "sources/forge"
    sources.mkdir(parents=True, exist_ok=True)
    (report / "pages").mkdir(exist_ok=True)
    contracts = report / "static/contracts"
    contracts.mkdir(parents=True, exist_ok=True)
    inputs = {"kpi_catalog.json": root / "models/kpi_catalog.json", "semantic_model.json": root / "models/semantic_model.json",
              "truth_manifest.json": root / "truth_manifest.json", "pipeline_evidence.json": state / "run_evidence.json",
              "manifest.json": state / "dbt/target/manifest.json", "run_results.json": state / "dbt/target/run_results.json",
              "reconciliation.json": state / "reconciliation.json", "product_design.json": root / "factory/product_design.json"}
    for name, source in inputs.items():
        shutil.copyfile(source, contracts / name)
    reconciliation = read(state / "reconciliation.json")
    catalog = read(root / "models/kpi_catalog.json")
    # The canonical KPI mart is consumed unchanged; catalog expressions are displayed, never evaluated.
    with duckdb.connect() as connection:
        for path in (state / "lake/gold").glob("*.parquet"):
            connection.execute(f"COPY (SELECT * FROM read_parquet({literal(path.as_posix())})) TO {literal((sources / (path.stem + '.csv')).as_posix())} (HEADER, DELIMITER ',')")
    csv_rows(sources / "kpi_reconciliation.csv", [{"kpi": k["name"], "id": k["id"], "description": k["description"],
             "actual": reconciliation["kpis"][k["id"]]["actual"], "expected": reconciliation["kpis"][k["id"]]["expected"],
             "matched": reconciliation["kpis"][k["id"]]["matched"], "source_model": k["sourceModel"]} for k in catalog["kpis"]],
             ["kpi", "actual", "expected", "matched", "id", "description", "source_model"])
    counts = read(state / "silver_counts.json")
    rows = [{"layer": layer, "table_name": table, "row_count": count} for layer, tables in counts.items() for table, count in tables.items()]
    rows += [{"layer": "gold", "table_name": table, "row_count": count} for table, count in reconciliation["goldRowCounts"].items()]
    csv_rows(sources / "row_counts.csv", rows, ["layer", "table_name", "row_count"])
    manifest = read(state / "dbt/target/manifest.json")
    results = read(state / "dbt/target/run_results.json")
    csv_rows(sources / "dbt_results.csv", [{"node": r["unique_id"], "status": r["status"], "execution_seconds": r["execution_time"],
             "resource_type": manifest["nodes"].get(r["unique_id"], {}).get("resource_type", "hook")} for r in results["results"]],
             ["node", "status", "execution_seconds", "resource_type"])
    csv_rows(sources / "dbt_lineage.csv", [{"node": n, "depends_on": parent, "schema_name": node["schema"]}
             for n, node in manifest["nodes"].items() for parent in node.get("depends_on", {}).get("nodes", [])], ["node", "depends_on", "schema_name"])
    truth = read(root / "truth_manifest.json")
    csv_rows(sources / "quality_evidence.csv", [{"evidence_id": e["evidenceId"], "injector": e["injector"], "entity": e["entity"],
             "record_keys": ", ".join(e["recordKeys"]), "expected_effect": e["expectedEffect"]} for e in truth["evidence"]],
             ["evidence_id", "injector", "entity", "record_keys", "expected_effect"])
    csv_rows(sources / "stages.csv", [{"stage": name, "status": r["status"], "started_at": r["startedAt"], "completed_at": r.get("completedAt", "")}
             for name, r in evidence["stages"].items() if name != "bi"], ["stage", "status", "started_at", "completed_at"])
    semantic = read(root / "models/semantic_model.json")
    write(contracts / "semantic_model.json", semantic)
    csv_rows(sources / "semantic_tables.csv", semantic["tables"], ["name", "role", "key", "labelColumn"])
    csv_rows(sources / "semantic_relationships.csv", semantic["relationships"], ["from", "to", "cardinality", "active"])
    write(report / "package.json", {"name": "contoso-forge-validation", "version": "1.5.0", "private": True, "type": "module",
          "scripts": {"sources": "evidence sources --strict", "build": "evidence build:strict", "dev": "evidence dev", "preview": "evidence preview"},
          "dependencies": {"@evidence-dev/evidence": "40.1.8", "@evidence-dev/core-components": "5.4.2", "@evidence-dev/csv": "1.0.16", "typescript": "5.4.2"}})
    (report / "evidence.config.yaml").write_text('appearance:\n  default: light\nplugins:\n  components:\n    "@evidence-dev/core-components": {}\n  datasources:\n    "@evidence-dev/csv": {}\n', encoding="utf-8")
    (sources / "connection.yaml").write_text("name: forge\ntype: csv\n", encoding="utf-8")
    page = """---
title: Contoso Forge · BI & Validation
---

# Business results, with proof

Gold is the source of every business number on this page. The C# truth manifest,
dbt tests and run evidence independently explain and verify those numbers.

"""
    page += f"Run **{evidence['runId']}** · dataset `{truth['datasetFingerprint']}`\n\n"
    page += """## Business KPIs

```sql kpis
select * from forge.kpi_reconciliation
```
<DataTable data={kpis} />

## Daily business results

The daily/store mart defines these measures in dbt. Select a row to inspect its grain.

```sql daily
select * from forge.bi_daily_customer_experience order by order_day, store_key
```
<LineChart data={daily} x=order_day y=sales_amount series=store_key title="Daily sales by store" />
<DataTable data={daily} search=true />

## Source → Silver → Gold

```sql counts
select * from forge.row_counts order by layer, table_name
```
<DataTable data={counts} />

## Data behavior and quarantine

```sql quality
select * from forge.quality_evidence
```
<DataTable data={quality} />

## dbt models and tests

```sql tests
select * from forge.dbt_results order by resource_type, node
```
<DataTable data={tests} search=true />

```sql lineage
select * from forge.dbt_lineage
```
<DataTable data={lineage} search=true />

## Pipeline stages

This snapshot covers the completed stages before report generation. The authoritative
run record is `run_evidence.json`; rendering is recorded separately in `bi/build_evidence.json`.

```sql stages
select * from forge.stages
```
<DataTable data={stages} />

## Semantic model

```sql semantic_tables
select * from forge.semantic_tables
```
<DataTable data={semantic_tables} />

```sql semantic_relationships
select * from forge.semantic_relationships
```
<DataTable data={semantic_relationships} />

"""
    ml = state / "ml/metrics.json"
    if ml.exists():
        metrics = read(ml)
        if metrics["status"] != "executed": raise ValueError("Report cannot present unexecuted ML metrics")
        inputs["ml_metrics.json"] = ml
        shutil.copyfile(ml, contracts / "ml_metrics.json")
        csv_rows(sources / "ml_metrics.csv", [{"algorithm": a, "split": split, **{k: v for k, v in m.items() if k not in ("pr_curve", "confusion_matrix")}}
                 for a, splits in metrics["models"].items() for split, m in splits.items()],
                 ["algorithm", "split", "average_precision", "roc_auc", "f1", "precision", "recall", "threshold"])
        csv_rows(sources / "ml_partitions.csv", [{"split": name, **p} for name, p in metrics["partitions"].items()],
                 ["split", "rows", "negative", "positive", "prevalence", "predictionStart", "predictionEnd", "latestLabel"])
        csv_rows(sources / "confusion_matrix.csv", [{"algorithm": a, "split": split, "actual": y, "predicted": p, "rows": m["confusion_matrix"][y][p]}
                 for a, splits in metrics["models"].items() for split, m in splits.items() for y in (0, 1) for p in (0, 1)], ["algorithm", "split", "actual", "predicted", "rows"])
        pd.read_parquet(state / "ml/feature_importance.parquet").to_csv(sources / "feature_importance.csv", index=False)
        for title, query in [("Measured model comparison", "ml_metrics"), ("Chronological partitions · 14-day embargo", "ml_partitions"), ("Confusion matrix · threshold 0.5", "confusion_matrix"), ("Validation permutation importance", "feature_importance")]:
            page += f"## {title}\n\n```sql {query}\nselect * from forge.{query}\n```\n<DataTable data={{{query}}} />\n\n"
    else:
        page += "## ML\n\nNo measured training results are attached to this run. BI validation is available independently of ML.\n\n"
    page += "## Inspect the contracts\n\n" + "\n".join(f"- [{name}](/contracts/{name})" for name in inputs) + "\n"
    (report / "pages/index.md").write_text(page, encoding="utf-8")
    contract = {"status": "package-generated", "renderStatus": "not-built", "target": "evidence", "runId": evidence["runId"],
                "inputHashes": {name: sha(source) for name, source in inputs.items()}, "goldHashes": {p.name: sha(p) for p in (state / "lake/gold").glob("*.parquet")},
                "pipelineSnapshot": "before BI completion; authoritative final status is ../run_evidence.json",
                "kpiLogic": "canonical dbt Gold only; no report-level KPI expressions", "ml": "measured" if ml.exists() else "absent"}
    contract["reportFileHashes"] = {p.relative_to(report).as_posix(): sha(p) for p in report.rglob("*") if p.is_file()}
    write(state / "bi/report_contract.json", contract)
    return {"status": "package-generated", "target": "evidence", "artifact": "bi/evidence", "renderStatus": "not-built", "contract": "bi/report_contract.json"}
