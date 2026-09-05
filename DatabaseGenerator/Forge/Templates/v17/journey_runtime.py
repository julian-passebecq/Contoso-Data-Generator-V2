"""Adapters behind the single C#-compiled V1.7 activity graph."""
from pathlib import Path
import shutil
from common import read, write, sha, now, identifier


def dispatch(root, state, product, settings, stage, evidence):
    import dbt_runtime
    from layers import bronze, silver, verify_bronze
    if stage == "verify": return {"status": "verified", **evidence["identity"]}
    if stage == "bronze": return bronze(root, state, settings["engine"])
    if stage == "silver": return {"status": "executed", **silver(root, state, settings["engine"])}
    if stage == "validate-silver":
        from duckdb_silver import validate
        verify_bronze(root, state)
        return validate(root, state)
    if stage == "wrangle":
        from wrangling import execute
        return execute(root, state, product["recipe"], settings["engine"])
    remote = state / "motherduck_build.json"
    if remote.exists() and read(remote)["status"] == "exported-not-executed":
        if stage == "publish":
            from motherduck import dive
            target = next(p for p in product["publishTargets"] if p["kind"] == "motherduck")
            if target["createDive"]:
                return {"status": "exported-not-executed", "blocksDownstream": True, "dive": dive(root, state, target)}
        return {"status": "exported-not-executed", "blocksDownstream": True, "reason": "MotherDuck Gold has not executed; dependent stage remains unexecuted"}
    if stage == "dbt":
        if settings["warehouse"] == "motherduck":
            from motherduck import publish_silver_and_build
            return publish_silver_and_build(root, state, next(p for p in product["publishTargets"] if p["kind"] == "motherduck"))
        if product["dbtIntegration"] == "cosmos":
            from orchestration import adopt_cosmos_dbt_results
            return adopt_cosmos_dbt_results(root, state, evidence["runId"])
        return dbt_runtime.build(root, state)
    if stage == "reconcile":
        database = "md:" + read(remote)["database"] if remote.exists() else None
        result = dbt_runtime.reconcile(root, state, database_path=database)
        if remote.exists():
            record = read(remote)
            record.update(status="executed", reconciliation=result, completedAt=now())
            # Keep the original dbt-stage receipt immutable; reconcile owns its own receipt.
            write(state / "motherduck_reconciliation.json", record)
            write(state / "motherduck_execution.json", record)
        return result
    if stage == "semantic": return semantic(root, state, settings)
    if stage == "analysis":
        if product["analysis"]["kind"] == "none": return {"status": "executed", "origin": "governed-kpi-query", **query(root, state, settings)}
        from external_labs import analysis
        return analysis(root, state, product["analysis"])
    if stage == "bi":
        from journey_report import build
        return build(root, state, evidence)
    if stage == "publish":
        results = {}
        for target in product["publishTargets"]:
            if target["kind"] == "huggingface":
                from huggingface_export import export_publish
                results["huggingface"] = export_publish(root, state, target)
            elif target["createDive"]:
                from motherduck import dive
                results["motherduck"] = dive(root, state, target)
        return {"status": "published" if results and all(r["status"] == "published" for r in results.values()) else "exported-not-published", "targets": results}
    raise ValueError("Unsupported V1.7 operation")


def query(root, state, settings):
    import duckdb
    catalog = read(root / "models/kpi_catalog.json")
    keys = [k["id"] for k in catalog["kpis"]]
    if not keys: return {"status": "executed", "kpis": {}, "warehouse": settings["warehouse"]}
    database = "md:" + read(state / "motherduck_build.json")["database"] if settings["warehouse"] == "motherduck" else str(state / "warehouse.duckdb")
    with duckdb.connect(database, read_only=settings["warehouse"] != "motherduck") as db:
        row = db.execute("SELECT " + ", ".join(identifier(k) for k in keys) + " FROM gold.kpi_customer_satisfaction").fetchone()
    from common import compare_kpis
    return {"status": "reconciled", "warehouse": database, "kpis": compare_kpis(dict(zip(keys, row)), read(root / "truth_manifest.json"), catalog)}


def semantic(root, state, settings):
    result = query(root, state, settings)
    for name in ("semantic_model.json", "kpi_catalog.json", "lineage.json", "query_examples.sql"):
        target = state / "models" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / "models" / name, target)
    result.update(runId=read(state / "run_evidence.json")["runId"], datasetFingerprint=read(root / "truth_manifest.json")["datasetFingerprint"],
        inputHashes={name: sha(root / "models" / name) for name in ("semantic_model.json", "lineage.json", "kpi_catalog.json", "query_examples.sql")})
    write(state / "semantic_execution.json", result)
    return result
