"""Explicit MotherDuck Lite execution/export. No authentication or writes at import."""
import argparse
import os
from pathlib import Path
import shutil
from common import read, write, sha, now, identifier, literal


def execute(root, run_id, database, create_dive=False):
    import duckdb
    import duckdb_silver
    import dbt_runtime
    from run import identity, state_path
    from bi_report import build as report
    root = root.resolve()
    project = read(root / "resolved_project.json")
    if project["settings"]["warehouse"] != "motherduck" or project.get("product", {}).get("version") != "1.5":
        raise ValueError("Compile a V1.5 motherduck-lite project first")
    identifier(database)
    if not os.environ.get("MOTHERDUCK_TOKEN"):
        raise ValueError("Set MOTHERDUCK_TOKEN externally; credentials are never stored in project or evidence")
    state = state_path(root, run_id)
    if state.exists(): raise ValueError("MotherDuck execution requires a fresh run ID and a new destination database")
    state.mkdir(parents=True)
    evidence = {"contractVersion": "1.5", "runId": run_id, "identity": identity(root), "startedAt": now(), "status": "running", "stages": {}, "warehouse": database}
    def stage(name, function):
        record = {"status": "running", "startedAt": now()}
        evidence["stages"][name] = record
        write(state / "run_evidence.json", evidence)
        try:
            record.update(result=function(), status="succeeded", completedAt=now())
        except Exception as error:
            record.update(status="failed", error=str(error), completedAt=now())
            evidence["status"] = "failed"
            raise
        finally: write(state / "run_evidence.json", evidence)
    stage("silver", lambda: duckdb_silver.transform(root, state))
    stage("validate-silver", lambda: duckdb_silver.validate(root, state))
    def publish():
        counts = {}
        with duckdb.connect("md:") as db:
            # CREATE without IF NOT EXISTS refuses accidental replacement of an existing warehouse.
            db.execute(f"CREATE DATABASE {identifier(database)}")
            db.execute(f"CREATE SCHEMA {identifier(database)}.silver")
            for table, expected in read(root / "truth_manifest.json")["expectedSilverRowCounts"].items():
                relation = f"{identifier(database)}.silver.{identifier(table)}"
                db.execute(f"CREATE TABLE {relation} AS SELECT * FROM read_parquet(?)", [str(state / "lake/silver" / table / "*.parquet")])
                counts[table] = db.execute(f"SELECT count(*) FROM {relation}").fetchone()[0]
                if counts[table] != expected: raise ValueError("Native MotherDuck Silver count mismatch: " + table)
        return {"status": "executed", "origin": "native-motherduck", "silverCounts": counts}
    stage("publish-silver", publish)
    dbt = dbt_runtime.prepare(root, state)
    # Silver is physically published to MotherDuck; do not replace it with local Parquet views.
    (dbt / "macros/register_silver_sources.sql").write_text("{% macro register_silver_sources() %}{{ return('') }}{% endmacro %}\n", encoding="utf-8")
    stage("dbt", lambda: dbt_runtime.build(root, state, database_path="md:" + database))
    stage("reconcile", lambda: dbt_runtime.reconcile(root, state, database_path="md:" + database))
    config = read(root / "factory/ml/run_config.json")
    if config["enabled"]:
        if config["target"] == "local-sklearn":
            from ml_lab import train
            stage("ml", lambda: train(root, state))
        else:
            from notebook_export import export
            stage("export-ml", lambda: export(root, state))
    dive = (root / "factory/dive.tsx").read_text(encoding="utf-8").replace("__DATABASE__", database)
    (state / "dive.tsx").write_text(dive, encoding="utf-8")
    if create_dive:
        def deploy():
            with duckdb.connect("md:") as db:
                resources = "[{'url': " + literal("md:" + database) + ", 'alias': " + literal(database) + "}]"
                row = db.execute("SELECT id FROM MD_CREATE_DIVE(title=?, content=?, description=?, api_version=1, required_resources=" + resources + ")",
                    ["Contoso Forge " + run_id, dive, "Canonical Gold/marts only; no report transformations"]).fetchone()
                return {"status": "created", "url": "https://app.motherduck.com/dives/" + str(row[0]), "sourceSha256": sha(state / "dive.tsx")}
        stage("dive", deploy)
    else:
        evidence["stages"]["dive"] = {"status": "exported", "source": "dive.tsx", "startedAt": now(), "completedAt": now()}
    stage("bi", lambda: report(root, state, evidence))
    evidence.update(status="succeeded", completedAt=now())
    write(state / "run_evidence.json", evidence)
    return evidence


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Publishes selected Silver to a NEW MotherDuck database, runs dbt-duckdb Gold/tests and reconciles native KPIs. Account quotas apply.")
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--database", required=True)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--create-dive", action="store_true", help="Create an internal Dive after native Gold reconciliation; embedding is not required")
    args = parser.parse_args()
    if not args.execute:
        print("Export only. Inspect factory/dive.tsx and factory/dbt. Supply --execute with an external MOTHERDUCK_TOKEN to publish to a NEW database; --create-dive is a separate explicit action.")
    else: execute(args.root, args.run_id, args.database, args.create_dive)
