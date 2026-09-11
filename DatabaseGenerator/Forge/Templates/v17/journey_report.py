"""Scoped Evidence presentation of governed KPI values and explicitly identified ML origins."""
import shutil
from copy import deepcopy
from decimal import Decimal
from common import read, write, sha
from bi_report import csv_rows


def display_value(value, format):
    value = Decimal(str(value))
    if format == "whole_number": return f"{value:,.0f}"
    if format == "currency_2dp": return f"{value:,.2f}"
    if format == "percentage_6dp": return f"{value:.6%}"
    return f"{value:,.6f}"


def build(root, state, evidence):
    report = state / "bi/evidence"
    sources, contracts = report / "sources/forge", report / "static/contracts"
    sources.mkdir(parents=True)
    contracts.mkdir(parents=True)
    (report / "pages").mkdir()
    # Immutable upstream snapshot avoids hashing final evidence into its own artifact.
    # Package generation and the later web build have separate authoritative receipts.
    snapshot = deepcopy(evidence)
    snapshot["status"] = "upstream-snapshot"
    snapshot["snapshotScope"] = "Stages completed before report package generation; excludes report and publication."
    snapshot["stages"] = {name: row for name, row in snapshot["stages"].items() if name != "bi"}
    write(state / "bi/upstream_evidence.json", snapshot)
    inputs = {"kpi_catalog.json": root / "models/kpi_catalog.json", "semantic_model.json": root / "models/semantic_model.json",
        "lineage.json": root / "models/lineage.json", "pipeline_evidence.json": state / "bi/upstream_evidence.json",
        "reconciliation.json": state / "reconciliation.json", "manifest.json": state / "dbt/target/manifest.json",
        "run_results.json": state / "dbt/target/run_results.json"}
    for name, path in inputs.items(): shutil.copyfile(path, contracts / name)
    reconciliation = read(state / "reconciliation.json")
    catalog = {k["id"]: k for k in read(root / "models/kpi_catalog.json")["kpis"]}
    if reconciliation["kpis"]:
        csv_rows(sources / "kpis.csv", [{"kpi": k, **v} for k, v in reconciliation["kpis"].items()], ["kpi", "actual", "expected", "matched"])
    csv_rows(sources / "stages.csv", [{"stage": name, "status": r["status"], "result": r.get("result", {}).get("status", "not-recorded")} for name, r in snapshot["stages"].items()], ["stage", "status", "result"])
    write(report / "package.json", {"name": "contoso-forge-journey", "version": "1.7.0", "private": True, "type": "module",
        "scripts": {"sources": "evidence sources --strict", "build": "evidence build:strict"},
        "dependencies": {"@evidence-dev/evidence": "40.1.8", "@evidence-dev/core-components": "5.4.2", "@evidence-dev/csv": "1.0.16", "typescript": "5.4.2"}})
    (report / "evidence.config.yaml").write_text('appearance:\n  default: light\nplugins:\n  components:\n    "@evidence-dev/core-components": {}\n  datasources:\n    "@evidence-dev/csv": {}\n', encoding="utf-8")
    (sources / "connection.yaml").write_text("name: forge\ntype: csv\n", encoding="utf-8")
    page = f"---\ntitle: Contoso Forge · {evidence['goal']}\n---\n\n# Governed results\n\nRun `{evidence['runId']}` · dataset `{evidence['identity']['datasetFingerprint']}`\n\n"
    if reconciliation["kpis"]:
        page += "| KPI | Actual | Expected | Matched |\n| --- | ---: | ---: | --- |\n"
        for key, values in reconciliation["kpis"].items():
            kpi = catalog[key]
            page += f"| {kpi['name']} | {display_value(values['actual'], kpi['format'])} | {display_value(values['expected'], kpi['format'])} | {values['matched']} |\n"
        page += "\nCurrency values use two decimal places; the catalog does not specify a currency code.\n\n"
    page += "## Upstream execution snapshot\n\nThese stages completed before report package generation. The linked pipeline evidence has the same scope. Report generation and publication are excluded. This page does not certify a successful web build; consult the run's `bi/build_evidence.json` receipt for its build outcome and final `run_evidence.json` for the complete pipeline.\n\n"
    page += "```sql stages\nselect * from forge.stages\n```\n<DataTable data={stages} />\n\n"
    metrics_path = state / "ml/metrics.json"
    if metrics_path.exists():
        metrics = read(metrics_path)
        page += f"## Measured ML\n\nOrigin: **{metrics['framework']}**. Selected model: **{metrics['selectedModel']}**. Selection: {metrics['selectedBy']}.\n\n"
        rows = [{"algorithm": name, "split": split, "origin": metrics["framework"], **values} for name, splits in metrics["models"].items() for split, values in splits.items()]
        page += "Operating point: stored baseline probability threshold (shown per row). Alternative validation-selected thresholds remain in the linked metrics artifact.\n\n"
        csv_rows(sources / "ml.csv", rows, ["algorithm", "split", "origin", "threshold", "average_precision", "roc_auc", "f1", "precision", "recall"])
        page += "```sql ml\nselect * from forge.ml\n```\n<DataTable data={ml} />\n\n"
        inputs["metrics.json"] = metrics_path
        shutil.copyfile(metrics_path, contracts / "metrics.json")
    else:
        page += "## Analysis status\n\nNo measured ML results are attached. Exported notebooks are unexecuted until validated results return.\n\n"
    page += "## Lineage and evidence\n\nShared source/dbt dependencies may build in full; published KPIs are scoped to the selection.\n\n" + "\n".join(f"- [{name}](/contracts/{name})" for name in inputs) + "\n"
    (report / "pages/index.md").write_text(page, encoding="utf-8")
    contract = {"status": "package-generated", "renderStatus": "not-built", "target": "evidence", "runId": evidence["runId"],
        "inputHashes": {name: sha(path) for name, path in inputs.items()}, "goldHashes": {},
        "reportFileHashes": {p.relative_to(report).as_posix(): sha(p) for p in report.rglob("*") if p.is_file()},
        "kpiLogic": "canonical dbt Gold columns only", "ml": "measured" if metrics_path.exists() else "absent"}
    write(state / "bi/report_contract.json", contract)
    return {"status": "package-generated", "renderStatus": "not-built", "contract": "bi/report_contract.json"}
