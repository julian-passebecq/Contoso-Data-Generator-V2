"""V1.7 MotherDuck destination: validated selected Silver, fresh database, one native dbt build."""
import os
from common import read, write, sha, now, identifier, literal


def publish_silver_and_build(root, state, target):
    from run import identity, verify_artifacts
    from layers import verify_bronze
    import dbt_runtime
    run = read(state / "run_evidence.json")
    if identity(root) != run["identity"]: raise ValueError("Stale MotherDuck source")
    verify_bronze(root, state)
    for stage in ("silver", "validate-silver"):
        if run["stages"].get(stage, {}).get("status") != "succeeded": raise ValueError("MotherDuck requires validated Silver")
        verify_artifacts(state, run["stages"][stage])
    database = target["database"]
    identifier(database)
    result = {"contractVersion": "1.7", "runId": run["runId"], "identity": run["identity"], "database": database,
              "status": "exported-not-executed", "startedAt": now(), "silverEngine": read(root / "resolved_project.json")["settings"]["engine"],
              "silverHashes": run["stages"]["silver"]["artifacts"], "freshDatabaseRequired": True}
    path = state / "motherduck_execution.json"
    if not target["execute"] or not os.environ.get("MOTHERDUCK_TOKEN"):
        result.update(reason="Execution not selected or MOTHERDUCK_TOKEN unavailable", blocksDownstream=True, completedAt=now())
        write(path, result)
        write(state / "motherduck_build.json", result)
        if target["requireExecution"]: raise ValueError("Required MotherDuck authentication unavailable")
        return result
    try:
        import duckdb
        counts = {}
        with duckdb.connect("md:") as db:
            db.execute(f"CREATE DATABASE {identifier(database)}")
            db.execute(f"CREATE SCHEMA {identifier(database)}.silver")
            for table, expected in read(root / "truth_manifest.json")["expectedSilverRowCounts"].items():
                relation = f"{identifier(database)}.silver.{identifier(table)}"
                db.execute(f"CREATE TABLE {relation} AS SELECT * FROM read_parquet(?)", [str(state / "lake/silver" / table / "*.parquet")])
                counts[table] = db.execute(f"SELECT count(*) FROM {relation}").fetchone()[0]
                if counts[table] != expected: raise ValueError("MotherDuck Silver row count mismatch")
        project = dbt_runtime.prepare(root, state)
        (project / "macros/register_silver_sources.sql").write_text("{% macro register_silver_sources() %}{{ return('') }}{% endmacro %}\n", encoding="utf-8")
        dbt = dbt_runtime.build(root, state, database_path="md:" + database)
        manifest = read(state / "dbt/target/manifest.json")
        results = read(state / "dbt/target/run_results.json")
        if results["metadata"]["invocation_id"] != manifest["metadata"]["invocation_id"]: raise ValueError("MotherDuck dbt invocation mismatch")
        result.update(status="dbt-executed-awaiting-reconciliation", silverCounts=counts, dbt=dbt,
            dbtInvocationId=results["metadata"]["invocation_id"], warehouse="md:" + database, completedAt=now())
    except Exception as error:
        result.update(status="failed", errorType=type(error).__name__, completedAt=now())
        # dbt may print a connection string on failure; redact external secrets before retaining logs.
        for log in state.rglob("*.log"):
            text = log.read_text(encoding="utf-8", errors="replace")
            token = os.environ.get("MOTHERDUCK_TOKEN")
            if token: log.write_text(text.replace(token, "[REDACTED]"), encoding="utf-8")
        raise RuntimeError("MotherDuck execution failed; inspect sanitized evidence") from None
    finally:
        write(path, result)
        write(state / "motherduck_build.json", result)
    return result


def dive(root, state, target):
    database = target["database"]
    source = (root / "factory/dive.tsx").read_text(encoding="utf-8").replace("__DATABASE__", database)
    # V1.7 KPI projection is the only published Dive source.
    keys = [k["id"] for k in read(root / "models/kpi_catalog.json")["kpis"]]
    query = "SELECT " + ", ".join(identifier(k) for k in keys) + f" FROM {identifier(database)}.gold.kpi_customer_satisfaction"
    # Keep the native Dive template/package and use a scoped Gold query, never recompute a KPI.
    write(state / "dive_query.json", {"query": query, "source": "canonical Gold", "selectedKpis": keys})
    import json
    source = '''import { useSQLQuery } from "@motherduck/react-sql-query";
export default function Dive() {
  const result = useSQLQuery(QUERY);
  if (result.error) return <div role="alert">Governed Gold could not be read.</div>;
  return <main><h1>Contoso Forge · Selected KPIs</h1><pre>{JSON.stringify(result.data, null, 2)}</pre></main>;
}
'''.replace("QUERY", json.dumps(query))
    (state / "dive.tsx").write_text(source, encoding="utf-8")
    result = {"status": "exported-not-published", "sourceSha256": sha(state / "dive.tsx"), "database": database}
    if target["execute"] and os.environ.get("MOTHERDUCK_TOKEN"):
        record = read(state / "motherduck_reconciliation.json")
        if record["status"] != "executed" or record["database"] != database: raise ValueError("Dive requires native reconciled MotherDuck Gold")
        import duckdb
        try:
            with duckdb.connect("md:") as db:
                resources = "[{'url': " + literal("md:" + database) + ", 'alias': " + literal(database) + "}]"
                row = db.execute("SELECT id FROM MD_CREATE_DIVE(title=?, content=?, description=?, api_version=1, required_resources=" + resources + ")",
                    ["Contoso Forge " + record["runId"], source, "Selected canonical Gold KPIs"]).fetchone()
                if not row or not row[0]: raise ValueError("No Dive identity returned")
            result.update(status="published", url="https://app.motherduck.com/dives/" + str(row[0]), completedAt=now())
        except Exception: raise RuntimeError("Dive publication failed") from None
    write(state / "motherduck_dive_execution.json", result)
    if (state / "motherduck_reconciliation.json").exists():
        write(state / "motherduck_execution.json", {**read(state / "motherduck_reconciliation.json"), "dive": result})
    return result
